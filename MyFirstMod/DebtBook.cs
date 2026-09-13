using System;
using System.Collections.Generic;

namespace MyFirstMod
{
    // Pure, dependency-free issuer debt book (audit Schema v5 spec, sections 2-4,
    // 7, 8). It owns the city's issued bonds and is the single source of truth for
    // servicing, primary placement, repayment, the capacity base, and the default
    // lifecycle. No Unity / Cities: Skylines dependencies, so it is fully
    // unit-testable on CI - which is where every P0 in this area is proven.
    //
    // The load-bearing guarantee (invariant I7): no method here writes
    // PlacedFraction except PlacePrimary. Repayment only moves OutstandingPrincipal
    // and Arrears down. That single property is what makes the P0-3 money loop
    // structurally impossible.
    public class DebtBook
    {
        public const float EPS = 0.01f;
        private const int MAX_REDEEMED_HISTORY = 16;

        private readonly List<Bond> _bonds = new List<Bond>();
        private readonly List<Bond> _redeemed = new List<Bond>();
        // O(1) active-bond lookup by Id (plan section 4). Kept in lock-step with
        // _bonds by every mutator below.
        private readonly Dictionary<string, Bond> _byId = new Dictionary<string, Bond>();
        private int _lastDefaultPeriod = -1;

        // Active, serviceable issues. Redeemed bonds are retained separately for
        // history (audit spec: REDEEMED is terminal, retained, not serviced).
        public List<Bond> Bonds { get { return _bonds; } }
        public List<Bond> Redeemed { get { return _redeemed; } }
        public int Count { get { return _bonds.Count; } }

        public int LastDefaultPeriod
        {
            get { return _lastDefaultPeriod; }
            set { _lastDefaultPeriod = value; }
        }

        public void Clear()
        {
            _bonds.Clear();
            _redeemed.Clear();
            _byId.Clear();
            _lastDefaultPeriod = -1;
        }

        public void Add(Bond b)
        {
            _bonds.Add(b);
            if (b != null && b.Id != null) _byId[b.Id] = b;
        }

        public Bond FindActive(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Bond b;
            return _byId.TryGetValue(id, out b) ? b : null;
        }

        // Remove an active bond from both the list and the index.
        private void RemoveActiveAt(int i)
        {
            Bond b = _bonds[i];
            _bonds.RemoveAt(i);
            if (b != null && b.Id != null) _byId.Remove(b.Id);
        }

        // ---- totals and bases (audit spec section 2: one base per concept) ----

        public float TotalOutstandingPrincipal
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < _bonds.Count; i++) t += _bonds[i].OutstandingPrincipal;
                return t;
            }
        }

        public float TotalArrears
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < _bonds.Count; i++) t += _bonds[i].Arrears;
                return t;
            }
        }

        // Debt owed = outstanding principal + arrears across the book (spec section 8).
        public float TotalDebtOwed { get { return TotalOutstandingPrincipal + TotalArrears; } }

        // Active scheduled debt service this period (coupon on outstanding principal).
        public float PeriodCouponTotal(int periodsPerYear)
        {
            float t = 0f;
            for (int i = 0; i < _bonds.Count; i++)
                t += _bonds[i].PeriodCoupon(periodsPerYear);
            return t;
        }

        // ---- lifecycle predicates (spec section 4) ----

        public bool AnyDefaulted
        {
            get
            {
                for (int i = 0; i < _bonds.Count; i++)
                    if (_bonds[i].State == BondState.Defaulted) return true;
                return false;
            }
        }

        // Issuance is suspended while any bond is defaulted OR any arrears exist.
        public bool IssuanceSuspended
        {
            get { return AnyDefaulted || TotalArrears > EPS; }
        }

        // Rating may recover only once nothing is defaulted, all arrears are
        // cleared, AND the lock-out window since the last default has elapsed.
        public bool RatingRecoveryAllowed(int currentPeriod, int lockoutPeriods)
        {
            if (AnyDefaulted) return false;
            if (TotalArrears > EPS) return false;
            if (_lastDefaultPeriod < 0) return true;
            return (currentPeriod - _lastDefaultPeriod) >= lockoutPeriods;
        }

        // ---- primary placement (spec: SimulateCitizenTradingInternal) ----

        // Investors take up unplaced primary inventory. Returns the proceeds the
        // city receives (the caller moves that into the treasury). Raises
        // PlacedFraction AND OutstandingPrincipal by the same amount, so the city
        // owes exactly what it raised.
        public float PlacePrimary(float buyVolume)
        {
            if (buyVolume <= 0f) return 0f;

            float totalUnplaced = 0f;
            for (int i = 0; i < _bonds.Count; i++)
            {
                if (_bonds[i].State == BondState.Redeemed) continue;
                totalUnplaced += _bonds[i].UnplacedNotional;
            }
            if (totalUnplaced <= 0f) return 0f;

            float buyable = buyVolume < totalUnplaced ? buyVolume : totalUnplaced;
            float proceeds = 0f;
            for (int i = 0; i < _bonds.Count; i++)
            {
                Bond b = _bonds[i];
                if (b.State == BondState.Redeemed) continue;
                float unplaced = b.UnplacedNotional;
                if (unplaced <= 0f) continue;

                float share = unplaced / totalUnplaced;
                float bought = buyable * share;
                if (bought > unplaced) bought = unplaced;

                b.PlacedFraction += bought / b.FaceValue;
                if (b.PlacedFraction > 1f) b.PlacedFraction = 1f;
                b.OutstandingPrincipal += bought; // city now owes this principal
                proceeds += bought;
            }
            return proceeds;
        }

        // ---- servicing (spec: ServicePeriod) ----

        // Service one period against a cash budget (display units). Pays arrears,
        // then coupon, then principal, per bond, from a single shared budget; any
        // shortfall rolls into arrears and the bond transitions through the
        // lifecycle. Returns the total cash consumed (caller moves it from the
        // treasury), and reports how many bonds missed a payment / newly defaulted
        // this period for narrative/reporting.
        public float ServicePeriod(int currentPeriod, float cashBudget,
            int gracePeriods, float arrearsSpread, int lockoutPeriods, int periodsPerYear,
            out int missedPayments, out int newDefaults)
        {
            missedPayments = 0;
            newDefaults = 0;

            float budget = cashBudget < 0f ? 0f : cashBudget;
            float totalPaid = 0f;

            for (int i = _bonds.Count - 1; i >= 0; i--)
            {
                Bond b = _bonds[i];
                if (b.RemainingPeriods > 0) b.RemainingPeriods--;
                if (b.RemainingPeriods < 0) b.RemainingPeriods = 0;
                bool maturing = b.RemainingPeriods <= 0;

                // Unpaid arrears compound a penalty ABOVE the coupon rate, so
                // defaulting costs more than paying (reverses the old inversion).
                if (b.Arrears > 0f)
                    b.Arrears += b.Arrears * ((b.CouponRate + arrearsSpread) / periodsPerYear);

                float coupon = (b.OutstandingPrincipal * b.CouponRate) / periodsPerYear;
                float principalDue = maturing ? b.OutstandingPrincipal : 0f;
                float due = b.Arrears + coupon + principalDue;

                float pay = budget < due ? budget : due;
                if (pay < 0f) pay = 0f;
                budget -= pay;
                totalPaid += pay;

                float rem = pay;
                float arrearsPaid = rem < b.Arrears ? rem : b.Arrears;
                b.Arrears -= arrearsPaid; rem -= arrearsPaid;

                float couponPaid = rem < coupon ? rem : coupon;
                rem -= couponPaid;

                float principalPaid = 0f;
                if (maturing)
                {
                    principalPaid = rem < principalDue ? rem : principalDue;
                    b.OutstandingPrincipal -= principalPaid;
                    rem -= principalPaid;
                }

                b.CouponsReceived += arrearsPaid + couponPaid + principalPaid;

                float couponShort = coupon - couponPaid;
                float principalShort = maturing ? (principalDue - principalPaid) : 0f;
                // A failed maturity principal repayment is a HARD default (no grace);
                // a missed coupon uses the grace window. (Matches real municipal
                // behavior and the plan's TC-02.)
                bool hardDefault = maturing && principalShort > EPS;
                if (couponShort + principalShort > EPS)
                {
                    b.Arrears += couponShort + principalShort;
                    if (maturing) b.OutstandingPrincipal -= principalShort; // rolled into arrears -> ~0
                    missedPayments++;
                }

                BondState before = b.State;
                UpdateState(b, currentPeriod, gracePeriods, lockoutPeriods, hardDefault);
                if (b.State == BondState.Defaulted && before != BondState.Defaulted)
                    newDefaults++;

                if (b.State == BondState.Redeemed)
                {
                    RemoveActiveAt(i);
                    _redeemed.Add(b);
                    if (_redeemed.Count > MAX_REDEEMED_HISTORY) _redeemed.RemoveAt(0);
                }
            }
            return totalPaid;
        }

        private void UpdateState(Bond b, int currentPeriod, int gracePeriods, int lockoutPeriods, bool hardDefault)
        {
            bool hasArrears = b.Arrears > EPS;
            bool matured = b.RemainingPeriods <= 0;

            // Fully cleared and term elapsed -> terminal.
            if (!hasArrears && b.OutstandingPrincipal <= EPS && matured)
            {
                b.Arrears = 0f;
                b.OutstandingPrincipal = 0f;
                b.PeriodsInArrears = 0;
                b.State = BondState.Redeemed;
                return;
            }

            if (hasArrears)
            {
                b.PeriodsInArrears++;
                if (b.State == BondState.Active) b.State = BondState.Delinquent;
                if (hardDefault || (b.State == BondState.Delinquent && b.PeriodsInArrears >= gracePeriods))
                {
                    b.State = BondState.Defaulted;
                    b.DefaultedAtPeriod = currentPeriod;
                    if (currentPeriod > _lastDefaultPeriod) _lastDefaultPeriod = currentPeriod;
                }
            }
            else
            {
                b.PeriodsInArrears = 0;
                b.Arrears = 0f;
                if (b.State == BondState.Delinquent)
                {
                    b.State = BondState.Active;
                }
                else if (b.State == BondState.Defaulted)
                {
                    // The instrument's arrears are cleared, but the issuer stays in
                    // default until the lock-out window since it defaulted elapses.
                    if (currentPeriod - b.DefaultedAtPeriod >= lockoutPeriods)
                        b.State = BondState.Active;
                }
            }
        }

        // ---- repayment (spec: PayDebtPercent -> RepayPrincipal) ----

        public float AmountOwed(string id)
        {
            Bond b = FindActive(id);
            if (b == null) return 0f;
            return b.OutstandingPrincipal + b.Arrears;
        }

        // Retire a specific bond outright. The caller must have already moved the
        // cash (AmountOwed). Returns false if the bond is gone.
        public bool RemoveBond(string id)
        {
            for (int i = 0; i < _bonds.Count; i++)
            {
                if (_bonds[i].Id == id)
                {
                    RemoveActiveAt(i);
                    return true;
                }
            }
            return false;
        }

        // Repay from a cash budget (already capped at available cash by the caller):
        // fully retire every bond whose owed fits the budget, then partially pay
        // down the smallest remaining. Arrears are paid before principal, and
        // PlacedFraction is never touched (I7). Returns cash actually applied;
        // reports how many bonds were retired and whether a partial paydown
        // happened.
        public float RepayByBudget(int currentPeriod, float budget, int lockoutPeriods,
            out int retiredCount, out bool partialApplied)
        {
            retiredCount = 0;
            partialApplied = false;
            float spent = 0f;
            if (budget <= 0f) return 0f;

            // Full retirements first (largest-first is irrelevant; match prior
            // behavior of retiring any that fit).
            for (int i = _bonds.Count - 1; i >= 0; i--)
            {
                Bond b = _bonds[i];
                float owed = b.OutstandingPrincipal + b.Arrears;
                if (owed > budget) continue;
                budget -= owed;
                spent += owed;
                RemoveActiveAt(i);
                retiredCount++;
            }

            // Partial paydown of the smallest remaining if nothing was retired.
            if (retiredCount == 0 && budget > 0f && _bonds.Count > 0)
            {
                int smallest = 0;
                float smallestOwed = _bonds[0].OutstandingPrincipal + _bonds[0].Arrears;
                for (int i = 1; i < _bonds.Count; i++)
                {
                    float o = _bonds[i].OutstandingPrincipal + _bonds[i].Arrears;
                    if (o < smallestOwed) { smallest = i; smallestOwed = o; }
                }

                Bond sb = _bonds[smallest];
                float paydown = budget < smallestOwed ? budget : smallestOwed;
                if (paydown > 0f)
                {
                    float rem = paydown;
                    float arrearsPaid = rem < sb.Arrears ? rem : sb.Arrears;
                    sb.Arrears -= arrearsPaid; rem -= arrearsPaid;
                    sb.OutstandingPrincipal -= rem;
                    spent += paydown;

                    if (sb.Arrears <= EPS)
                    {
                        sb.Arrears = 0f;
                        sb.PeriodsInArrears = 0;
                        if (sb.State == BondState.Delinquent)
                            sb.State = BondState.Active;
                        else if (sb.State == BondState.Defaulted &&
                                 currentPeriod - sb.DefaultedAtPeriod >= lockoutPeriods)
                            sb.State = BondState.Active;
                    }

                    if (sb.OutstandingPrincipal + sb.Arrears < 1f)
                    {
                        RemoveActiveAt(smallest);
                        retiredCount++;
                    }
                    else
                    {
                        partialApplied = true;
                    }
                }
            }

            return spent;
        }

        // ---- invariants (spec section 7) ----

        public bool ValidateInvariants()
        {
            for (int i = 0; i < _bonds.Count; i++)
            {
                Bond b = _bonds[i];
                if (b.PlacedFraction < -EPS || b.PlacedFraction > 1f + EPS) return false;   // I1
                if (b.OutstandingPrincipal < -EPS) return false;                             // I2
                if (b.OutstandingPrincipal > b.FaceValue + 1f) return false;                 // I3
                if (b.OutstandingPrincipal > b.FaceValue * b.PlacedFraction + 1f) return false; // I4
                if (b.RemainingPeriods < 0) return false;                                    // I5
                if (b.State == BondState.Redeemed && b.OutstandingPrincipal > 1f) return false; // I6
                if (b.Arrears < -EPS) return false;                                          // I8
                if (b.PeriodsInArrears < 0) return false;                                    // I9
            }
            return true;
        }
    }
}
