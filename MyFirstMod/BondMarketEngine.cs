using System;
using System.Collections.Generic;
using ICities;
using ColossalFramework;
using UnityEngine;

namespace MyFirstMod
{
    public class BondMarketEngine : EconomyExtensionBase
    {
        public static BondMarketEngine Instance;
        public static bool NeedsReset;

        private const int WINDOW_SIZE = 60;
        public const int TICKS_PER_PERIOD = 15;
        private const int MIN_MARKET_BONDS = 4;
        private const int INTERNAL_UNIT_SCALE = 100;
        private const int MAX_ISSUED_BONDS = 5;
        private const float DEFAULT_YIELD_SPIKE = 0.012f;
        private const int DEFAULT_DECAY_PER_PERIOD = 1;

        private readonly float[] _cashFlowHistory = new float[WINDOW_SIZE];
        private int _windowIndex;
        private long _prevMoney;
        private bool _prevMoneySet;

        private readonly List<Bond> _marketBonds = new List<Bond>();
        private readonly List<Bond> _portfolioBonds = new List<Bond>();
        private readonly List<Bond> _issuedBonds = new List<Bond>();
        private readonly object _lock = new object();

        private int _tickCounter;
        private int _nextBondId;
        private bool _initialized;
        private int _defaultPenalty;
        private int _totalDefaults;
        private float _realizedPL;

        private float _grossIncome;
        private float _totalExpenses;
        private float _debtBurden;
        private float _dscr;
        private float _noi;
        private CreditRating _rating;
        private float _benchmarkRate;
        private float _requiredYield;

        private static readonly string[] ISSUE_NAMES = new string[]
        {
            "Emergency Note",
            "Municipal Note",
            "Revenue Bond",
            "Infrastructure Bond",
            "Capital Bond"
        };
        private static readonly float[] ISSUE_FACES = new float[]
        {
            25000f, 75000f, 200000f, 400000f, 750000f
        };
        private static readonly int[] ISSUE_PERIODS = new int[]
        {
            4, 6, 8, 10, 12
        };

        public float GrossIncome { get { return _grossIncome; } }
        public float TotalExpenses { get { return _totalExpenses; } }
        public float DebtBurden { get { return _debtBurden; } }
        public float DSCR { get { return _dscr; } }
        public float NOI { get { return _noi; } }
        public CreditRating Rating { get { return _rating; } }
        public float BenchmarkRate { get { return _benchmarkRate; } }
        public float RequiredYield { get { return _requiredYield; } }
        public int DefaultPenalty { get { return _defaultPenalty; } }
        public int TotalDefaults { get { return _totalDefaults; } }
        public float RealizedPL { get { return _realizedPL; } }
        public int TicksInCurrentPeriod { get { return _tickCounter; } }

        public int IssuedCount { get { lock (_lock) { return _issuedBonds.Count; } } }
        public int MaxIssuedBonds { get { return MAX_ISSUED_BONDS; } }
        public int IssueTemplateCount { get { return ISSUE_NAMES.Length; } }

        public override long OnUpdateMoneyAmount(long internalMoneyAmount)
        {
            Instance = this;

            if (NeedsReset)
            {
                NeedsReset = false;
                ResetState();
            }

            if (_prevMoneySet)
            {
                float delta = (float)(internalMoneyAmount - _prevMoney);
                _cashFlowHistory[_windowIndex] = delta;
                _windowIndex = (_windowIndex + 1) % WINDOW_SIZE;
            }
            _prevMoney = internalMoneyAmount;
            _prevMoneySet = true;

            RecalculateMetrics(internalMoneyAmount);

            _tickCounter++;
            if (_tickCounter >= TICKS_PER_PERIOD)
            {
                _tickCounter = 0;
                AgeBonds();
            }

            if (!_initialized)
            {
                _initialized = true;
                GenerateInitialBonds();
            }

            lock (_lock)
            {
                if (_marketBonds.Count < MIN_MARKET_BONDS)
                    RegenerateBonds();
            }

            return internalMoneyAmount;
        }

        private void RecalculateMetrics(long internalMoneyAmount)
        {
            float totalPositive = 0f;
            float totalNegative = 0f;
            int positiveCount = 0;

            for (int i = 0; i < WINDOW_SIZE; i++)
            {
                float v = _cashFlowHistory[i];
                if (v > 0f)
                {
                    totalPositive += v;
                    positiveCount++;
                }
                else if (v < 0f)
                {
                    totalNegative += -v;
                }
            }

            _grossIncome = totalPositive / INTERNAL_UNIT_SCALE;
            _totalExpenses = totalNegative / INTERNAL_UNIT_SCALE;

            float avgIncome = _grossIncome / WINDOW_SIZE;
            float avgExpense = _totalExpenses / WINDOW_SIZE;

            float estimatedDebtService = avgExpense * 0.15f;

            if (avgIncome > 0f)
                _debtBurden = estimatedDebtService / avgIncome;
            else
                _debtBurden = 1f;

            _noi = avgIncome - avgExpense + estimatedDebtService;
            if (estimatedDebtService > 0f)
                _dscr = _noi / estimatedDebtService;
            else
                _dscr = _noi > 0f ? 10f : 0f;

            float cashDisplay = (float)internalMoneyAmount / INTERNAL_UNIT_SCALE;
            if (cashDisplay > 500000f && _dscr < 3f)
                _dscr = Math.Min(_dscr + 1.0f, 10f);
            if (cashDisplay < 10000f && _dscr > 0.5f)
                _dscr = Math.Max(_dscr - 0.5f, 0f);

            _rating = BondPricing.CalculateRating(_debtBurden, _dscr);

            _benchmarkRate = 0.02f + _debtBurden * 0.08f;
            if (_benchmarkRate < 0.01f) _benchmarkRate = 0.01f;
            if (_benchmarkRate > 0.15f) _benchmarkRate = 0.15f;

            float baseYield = BondPricing.GetRequiredYield(_benchmarkRate, _rating);

            float defaultSpike = _defaultPenalty * (DEFAULT_YIELD_SPIKE / 25f);
            _requiredYield = baseYield + defaultSpike;
            if (_requiredYield > 0.50f) _requiredYield = 0.50f;
        }

        private void AgeBonds()
        {
            lock (_lock)
            {
                for (int i = _portfolioBonds.Count - 1; i >= 0; i--)
                {
                    Bond b = _portfolioBonds[i];
                    b.RemainingPeriods--;

                    if (b.RemainingPeriods <= 0)
                    {
                        int faceInternal = (int)(b.FaceValue * INTERNAL_UNIT_SCALE);
                        AddCashToCity(faceInternal);
                        _realizedPL += (b.FaceValue + b.CouponsReceived) - b.PurchasePrice;
                        _portfolioBonds.RemoveAt(i);
                        Debug.Log("[MyFirstMod] Bond matured: " + b.Name + " - returned face value " + b.FaceValue.ToString("N0"));
                    }
                    else
                    {
                        float couponPayment = (b.FaceValue * b.CouponRate) / BondPricing.PeriodsPerYear;
                        int couponInternal = (int)(couponPayment * INTERNAL_UNIT_SCALE);
                        if (couponInternal > 0)
                        {
                            AddCashToCity(couponInternal);
                            b.CouponsReceived += couponPayment;
                        }
                    }
                }

                for (int i = _marketBonds.Count - 1; i >= 0; i--)
                {
                    _marketBonds[i].RemainingPeriods--;
                    if (_marketBonds[i].RemainingPeriods <= 0)
                        _marketBonds.RemoveAt(i);
                }

                ServiceIssuedBonds();

                if (_defaultPenalty > 0)
                    _defaultPenalty = Math.Max(0, _defaultPenalty - DEFAULT_DECAY_PER_PERIOD);
            }
        }

        private void ServiceIssuedBonds()
        {
            for (int i = _issuedBonds.Count - 1; i >= 0; i--)
            {
                Bond ib = _issuedBonds[i];
                ib.RemainingPeriods--;

                if (ib.RemainingPeriods <= 0)
                {
                    int faceInternal = (int)(ib.FaceValue * INTERNAL_UNIT_SCALE);
                    if (!TrySpendCash(faceInternal))
                    {
                        TriggerDefault(ib, "maturity repayment");
                        _issuedBonds.RemoveAt(i);
                        continue;
                    }
                    ib.CouponsReceived += ib.FaceValue;
                    _issuedBonds.RemoveAt(i);
                    Debug.Log("[MyFirstMod] Issued bond matured - repaid " + ib.FaceValue.ToString("N0") + " on " + ib.Name);
                }
                else
                {
                    float couponPayment = (ib.FaceValue * ib.CouponRate) / BondPricing.PeriodsPerYear;
                    int couponInternal = (int)(couponPayment * INTERNAL_UNIT_SCALE);
                    if (couponInternal > 0)
                    {
                        if (!TrySpendCash(couponInternal))
                        {
                            TriggerDefault(ib, "coupon payment");
                            _issuedBonds.RemoveAt(i);
                            continue;
                        }
                        ib.CouponsReceived += couponPayment;
                    }
                }
            }
        }

        private void TriggerDefault(Bond bond, string reason)
        {
            _defaultPenalty += 3;
            _totalDefaults++;
            Debug.Log("[MyFirstMod] *** DEFAULT *** on " + bond.Name +
                " - failed " + reason +
                " | Yield spiked! Penalty: " + _defaultPenalty.ToString() +
                " | Total defaults: " + _totalDefaults.ToString());
        }

        public bool IssueBond(int templateIndex)
        {
            lock (_lock)
            {
                if (templateIndex < 0 || templateIndex >= ISSUE_NAMES.Length)
                    return false;
                if (_issuedBonds.Count >= MAX_ISSUED_BONDS)
                    return false;
                if (_rating == CreditRating.D)
                    return false;

                string name = ISSUE_NAMES[templateIndex];
                float face = ISSUE_FACES[templateIndex];
                int periods = ISSUE_PERIODS[templateIndex];
                float couponRate = _requiredYield;

                _nextBondId++;
                Bond bond = new Bond("I" + _nextBondId.ToString(), name, face, couponRate, periods);
                bond.PurchasePrice = face;
                _issuedBonds.Add(bond);

                int cashInternal = (int)(face * INTERNAL_UNIT_SCALE);
                AddCashToCity(cashInternal);

                Debug.Log("[MyFirstMod] Issued bond: " + name +
                    " | Raised: " + face.ToString("N0") +
                    " | Rate: " + (couponRate * 100f).ToString("F1") + "%" +
                    " | Term: " + periods.ToString() + " periods");
                return true;
            }
        }

        public string GetTemplateName(int index) { return ISSUE_NAMES[index]; }
        public float GetTemplateFace(int index) { return ISSUE_FACES[index]; }
        public int GetTemplatePeriods(int index) { return ISSUE_PERIODS[index]; }

        public bool CanIssueBonds
        {
            get
            {
                lock (_lock)
                {
                    return _issuedBonds.Count < MAX_ISSUED_BONDS && _rating != CreditRating.D;
                }
            }
        }

        public float TotalDebtOwed
        {
            get
            {
                lock (_lock)
                {
                    float total = 0f;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                    {
                        Bond ib = _issuedBonds[i];
                        float remainingCoupons = (ib.FaceValue * ib.CouponRate / BondPricing.PeriodsPerYear) * ib.RemainingPeriods;
                        total += ib.FaceValue + remainingCoupons;
                    }
                    return total;
                }
            }
        }

        public float TotalCouponsPaid
        {
            get
            {
                lock (_lock)
                {
                    float total = 0f;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                        total += _issuedBonds[i].CouponsReceived;
                    return total;
                }
            }
        }

        public string CreditStatusLabel
        {
            get
            {
                if (_defaultPenalty >= 12) return "IN DEFAULT - YIELD CRITICAL";
                if (_defaultPenalty >= 6) return "DISTRESSED - YIELD SPIKED";
                if (_defaultPenalty >= 3) return "UNDER PRESSURE";
                if (_defaultPenalty > 0) return "RECOVERING";
                return "GOOD STANDING";
            }
        }

        public int Buy10x1MBonds()
        {
            lock (_lock)
            {
                int bought = 0;
                for (int i = 0; i < 10; i++)
                {
                    float couponRate = _requiredYield;
                    int periods = 60;
                    _nextBondId++;
                    Bond bond = new Bond("B1M" + _nextBondId.ToString(), "1M Treasury Bond", 1000000f, couponRate, periods);
                    float price = BondPricing.PresentValue(bond, _requiredYield);
                    int priceInternal = (int)(price * INTERNAL_UNIT_SCALE);
                    if (!TrySpendCash(priceInternal))
                        break;
                    bond.PurchasePrice = price;
                    _portfolioBonds.Add(bond);
                    bought++;
                }
                if (bought > 0)
                    Debug.Log("[MyFirstMod] Bought " + bought.ToString() + "x 1M 5yr Treasury Bonds");
                return bought;
            }
        }

        public bool BuyBond(int marketIndex)
        {
            lock (_lock)
            {
                if (marketIndex < 0 || marketIndex >= _marketBonds.Count)
                    return false;

                Bond bond = _marketBonds[marketIndex];
                float price = BondPricing.PresentValue(bond, _requiredYield);

                int priceInternal = (int)(price * INTERNAL_UNIT_SCALE);
                if (!TrySpendCash(priceInternal))
                    return false;

                bond.PurchasePrice = price;
                _portfolioBonds.Add(bond);
                _marketBonds.RemoveAt(marketIndex);

                Debug.Log("[MyFirstMod] Bought bond: " + bond.Name + " for " + price.ToString("N0"));
                return true;
            }
        }

        public bool SellBond(int portfolioIndex)
        {
            lock (_lock)
            {
                if (portfolioIndex < 0 || portfolioIndex >= _portfolioBonds.Count)
                    return false;

                Bond bond = _portfolioBonds[portfolioIndex];
                float price = BondPricing.PresentValue(bond, _requiredYield);

                int priceInternal = (int)(price * INTERNAL_UNIT_SCALE);
                AddCashToCity(priceInternal);

                _realizedPL += (price + bond.CouponsReceived) - bond.PurchasePrice;
                _portfolioBonds.RemoveAt(portfolioIndex);
                Debug.Log("[MyFirstMod] Sold bond: " + bond.Name + " for " + price.ToString("N0"));
                return true;
            }
        }

        public int SellAllBonds()
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = _portfolioBonds.Count - 1; i >= 0; i--)
                {
                    Bond bond = _portfolioBonds[i];
                    float price = BondPricing.PresentValue(bond, _requiredYield);
                    int priceInternal = (int)(price * INTERNAL_UNIT_SCALE);
                    AddCashToCity(priceInternal);
                    _realizedPL += (price + bond.CouponsReceived) - bond.PurchasePrice;
                    _portfolioBonds.RemoveAt(i);
                    count++;
                }
                Debug.Log("[MyFirstMod] Sold all bonds: " + count.ToString() + " positions liquidated");
                return count;
            }
        }

        private bool TrySpendCash(int internalAmount)
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return false;

            long currentCash = em.LastCashAmount;
            if (currentCash < internalAmount)
                return false;

            em.FetchResource(EconomyManager.Resource.LoanPayment, internalAmount,
                ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
            return true;
        }

        private void AddCashToCity(int internalAmount)
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return;

            em.AddResource(EconomyManager.Resource.PublicIncome, internalAmount,
                ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
        }

        private void ResetState()
        {
            lock (_lock)
            {
                _marketBonds.Clear();
                _portfolioBonds.Clear();
                _issuedBonds.Clear();
            }
            for (int i = 0; i < WINDOW_SIZE; i++)
                _cashFlowHistory[i] = 0f;
            _windowIndex = 0;
            _prevMoney = 0;
            _prevMoneySet = false;
            _tickCounter = 0;
            _nextBondId = 0;
            _initialized = false;
            _defaultPenalty = 0;
            _totalDefaults = 0;
            _realizedPL = 0f;
            _grossIncome = 0f;
            _totalExpenses = 0f;
            _debtBurden = 0f;
            _dscr = 0f;
            _noi = 0f;
            _rating = CreditRating.AAA;
            _benchmarkRate = 0f;
            _requiredYield = 0f;
            Debug.Log("[MyFirstMod] Bond market state reset for new game.");
        }

        private void GenerateInitialBonds()
        {
            lock (_lock)
            {
                _marketBonds.Clear();
                _marketBonds.Add(MakeBond("City Infrastructure Note", 10000f, 0.03f, 2));
                _marketBonds.Add(MakeBond("Transit Revenue Bond", 25000f, 0.045f, 4));
                _marketBonds.Add(MakeBond("Education Fund Bond", 50000f, 0.05f, 6));
                _marketBonds.Add(MakeBond("Water & Sewer Bond", 75000f, 0.055f, 8));
                _marketBonds.Add(MakeBond("General Obligation Bond", 100000f, 0.06f, 10));
                _marketBonds.Add(MakeBond("Capital Improvement Bond", 200000f, 0.065f, 12));
            }
        }

        private void RegenerateBonds()
        {
            string[] names = new string[]
            {
                "Municipal Note", "Revenue Bond", "School District Bond",
                "Utility Bond", "Highway Bond", "Park Bond",
                "Hospital Bond", "Fire Station Bond", "Police Bond"
            };
            float[] faces = new float[] { 10000f, 25000f, 50000f, 75000f, 100000f };
            float[] coupons = new float[] { 0.03f, 0.04f, 0.045f, 0.05f, 0.055f, 0.06f };
            int[] periods = new int[] { 2, 4, 6, 8, 10, 12 };

            while (_marketBonds.Count < 6)
            {
                int ni = _nextBondId % names.Length;
                int fi = (_nextBondId * 3) % faces.Length;
                int ci = (_nextBondId * 7) % coupons.Length;
                int pi = (_nextBondId * 5) % periods.Length;

                _marketBonds.Add(MakeBond(names[ni], faces[fi], coupons[ci], periods[pi]));
            }
        }

        private Bond MakeBond(string name, float face, float coupon, int periods)
        {
            _nextBondId++;
            return new Bond("B" + _nextBondId.ToString(), name, face, coupon, periods);
        }

        public void GetMarketSnapshot(List<Bond> outBonds, List<float> outPrices)
        {
            outBonds.Clear();
            outPrices.Clear();
            lock (_lock)
            {
                float yield = _requiredYield;
                for (int i = 0; i < _marketBonds.Count; i++)
                {
                    outBonds.Add(_marketBonds[i]);
                    outPrices.Add(BondPricing.PresentValue(_marketBonds[i], yield));
                }
            }
        }

        public void GetPortfolioSnapshot(List<Bond> outBonds, List<float> outPrices)
        {
            outBonds.Clear();
            outPrices.Clear();
            lock (_lock)
            {
                float yield = _requiredYield;
                for (int i = 0; i < _portfolioBonds.Count; i++)
                {
                    outBonds.Add(_portfolioBonds[i]);
                    outPrices.Add(BondPricing.PresentValue(_portfolioBonds[i], yield));
                }
            }
        }

        public int PortfolioCount
        {
            get { lock (_lock) { return _portfolioBonds.Count; } }
        }

        public int MarketCount
        {
            get { lock (_lock) { return _marketBonds.Count; } }
        }
    }
}
