using System;
using System.Collections.Generic;
using System.IO;
using ICities;
using ColossalFramework;
using UnityEngine;

namespace MyFirstMod
{
    public class BondMarketEngine : EconomyExtensionBase
    {
        public static BondMarketEngine Instance;
        public static bool NeedsReset;
        public static byte[] PendingSaveData;

        private const int WINDOW_SIZE = 60;
        public const int TICKS_PER_PERIOD = 15;
        private const int MIN_MARKET_BONDS = 6;
        private const int INTERNAL_UNIT_SCALE = 100;
        private const int MAX_ISSUED_BONDS = 5;
        private const float DEFAULT_YIELD_SPIKE_PER_POINT = 0.0025f;
        private const int DEFAULT_DECAY_PER_PERIOD = 1;
        private const int DEFAULT_PENALTY_PER_EVENT = 12;
        private const int MAX_DEFAULT_PENALTY = 60;            // P0-2: cap so sustained default can't run the spike unbounded
        // Servicing triad: GRACE_PERIODS, ARREARS_SPREAD, and DEFAULT_LOCKOUT_PERIODS
        // are coupled. GRACE_PERIODS sets how many missed coupons before hard default;
        // ARREARS_SPREAD penalizes arrears during grace; DEFAULT_LOCKOUT_PERIODS blocks
        // new issuance after a default resolves. Changing one without the others skews
        // the default lifecycle — see DebtBook.ServicePeriod and UpdateState.
        private const int GRACE_PERIODS = 2;
        private const float ARREARS_SPREAD = 0.03f;
        private const int DEFAULT_LOCKOUT_PERIODS = 12;

        private readonly float[] _cashFlowHistory = new float[WINDOW_SIZE];
        private int _windowIndex;
        private int _cashSamples;          // number of organic deltas recorded so far
        private long _prevMoney;
        private bool _prevMoneySet;

        // P0-6: authoritative cash cursor. Seeded from the balance the game hands
        // us each tick (and from LastCashAmount at the start of each UI action),
        // then decremented/incremented by every mod cash op, so multiple ops in
        // one tick do not all read the same pre-tick balance.
        private long _tickCash;
        // P0-7: net cash the mod itself moved since the last cash-flow sample
        // (using the amounts the game actually moved, not the amounts requested).
        // Subtracted out in UpdateCashFlowHistory so the credit model never
        // mistakes the mod's own inflows/outflows for organic revenue.
        private long _modCashDeltaPending;

        // Phase 2 (P1-1/P1-9): smoothed per-tick operating income/expense that feed
        // the annualized credit model. Sourced from the game ledger when available
        // (EconomyReader), otherwise from the balance-delta proxy window.
        private float _avgIncomePerPeriod;
        private float _avgExpensePerPeriod;
        private int _totalTicks;
        private int _lastFlowSampleTick;
        private int _measuredTicksPerPeriod = TICKS_PER_PERIOD;
        private long _prevLedgerIncome;
        private long _prevLedgerExpense;
        private bool _ledgerBaselineSet;
        private float _monthsOfReserves;
        private bool _creditModelNoticePending; // one-time Phase 2 migration banner flag
        private bool _gameDataAvailable;        // P2-4: whether the game exposed usable demographics this pass
        private bool _periodMetricsInitialized; // P2-2: has the per-period metrics block run at least once
        private int _g2DiagSamples;             // gate G-2: how many of the three diagnostic samples have fired
        private int _metricRuns;                // count of metrics recomputes since load (for the G-2 diagnostic)

        private readonly List<Bond> _marketBonds = new List<Bond>();
        private readonly List<Bond> _portfolioBonds = new List<Bond>();
        // Schema v5: issued debt now lives in the pure DebtBook (single source of
        // truth for servicing, placement, repayment, lifecycle and capacity base).
        private readonly DebtBook _debtBook = new DebtBook();
        // Read-only alias so the engine's iteration/read sites keep working; all
        // MUTATIONS go through DebtBook methods, never this list directly.
        private List<Bond> _issuedBonds { get { return _debtBook.Bonds; } }
        private int _periodCounter; // monotonic period index driving the lifecycle
        private readonly float[] _placementBefore = new float[MAX_ISSUED_BONDS]; // reused per-period (plan section 4)
        private readonly System.Text.StringBuilder _detailBuilder = new System.Text.StringBuilder(64); // P2-3: reused
        private readonly object _lock = new object();
        private volatile EngineSnapshot _snapshot = new EngineSnapshot();
        // Phase 5 (G-1): deterministic, serializable PRNG so stochastic outcomes
        // (issuer migration/default) survive reload and can't be save-scummed.
        private DeterministicRandom _rng = new DeterministicRandom(unchecked((int)DateTime.Now.Ticks));
        private Random _cosmeticRng = new System.Random();

        // Phase 5 (P1-2): market issuers, each with its own migrating credit.
        private readonly List<MarketIssuer> _issuers = new List<MarketIssuer>();
        private float _hazardMultiplier = IssuerModel.HAZARD_STANDARD;
        private float _rateVolatilityScale = 1f;
        private bool _citizenTradingEnabled = true;
        private bool _revenueBondsEnabled; // quarantined until Gate B; default off
        private int _annualCounter;

        private int _tickCounter;
        private int _ticksThisPeriod;
        private int _lastGameMonth = -1;
        private int _nextBondId;
        private bool _initialized;
        private bool _resetInProgress; // P0-1: re-entrancy guard for ResetStateInternal
        private int _defaultPenalty;
        private int _totalDefaults;
        private double _realizedPL;

        private const int MAX_ACTIVE_SWAPS = 5;
        private readonly List<InterestRateSwap> _activeSwaps = new List<InterestRateSwap>();
        private int _nextSwapId;
        private float _revenueVolatility;
        private double _swapPL;

        private float _demandScore;
        private float _defaultProbability;
        private float _cityVitals;
        private float _financialHealth;
        private float _citizenConfidence;
        private float _bondAppeal;
        private float _absorptionCapacity;
        private int _population;
        private int _prevPopulation;
        private float _happiness;
        private float _health;
        private float _education;
        private float _landValue;
        private float _crimeRate;
        private float _employmentRate;
        private float _populationGrowth;
        private float _cashReserves;
        private MarketState _currentMarketState;
        private MarketState _previousMarketState;

        private float _citizenBuyVolume;
        private float _citizenSellVolume;
        private float _marketPressure;
        private float _smoothedPressure;
        private readonly float[] _pressureHistory = new float[12];
        private int _pressureHistoryIndex;
        private float _citizenProceedsThisPeriod;
        private double _totalCitizenProceeds;

        private readonly List<CimTransaction> _transactionLog = new List<CimTransaction>();
        private const int MAX_TRANSACTION_LOG = 50;
        private int _transactionSeq;

        private const int PERIODS_PER_QUARTER = 3;
        private const int MAX_REPORT_HISTORY = 8;
        private int _periodsSinceReport;
        private int _quarterNumber;
        private int _quarterDefaults;
        private readonly List<QuarterlyReport> _reportHistory = new List<QuarterlyReport>();

        private float _prevRequiredYield;

        private float _grossIncome;
        private float _totalExpenses;
        private float _debtBurden;
        private float _dscr;
        private float _noi;
        private CreditRating _rating;
        // P0-5: two rates that used to be one. _benchmarkRate / _marketFloatingRate
        // is the EXOGENOUS index - swaps and floating-rate debt settle against it
        // and nothing the city does moves it. _cityBorrowingRate layers the city's
        // own fiscal adjustment and over-hedge penalty on top and prices what the
        // city issues. Folding the over-hedge penalty into the swap index was what
        // turned the penalty into a self-reinforcing reward.
        private float _benchmarkRate;       // exogenous benchmark (shown to player)
        private float _marketFloatingRate;  // swap / floating-rate settlement index
        private float _cityBorrowingRate;   // benchmark + fiscal adj + over-hedge penalty
        private float _requiredYield;
        private float _portfolioValue;

        // Phase 4 (P1-4): mean-reverting short rate on a business cycle, and the
        // Nelson-Siegel curve derived from it (P1-3). Persisted so the market does
        // not jump on reload.
        private float _shortRate = 0.04f;
        private float _cyclePhase;
        private YieldCurve _yieldCurve;
        private const float RATE_KAPPA = 0.15f;             // mean-reversion speed (per year)
        private const float RATE_BASE_THETA = 0.04f;        // long-run mean of the short rate
        private const float RATE_CYCLE_AMPLITUDE = 0.015f;  // business-cycle swing in theta
        private const float RATE_SIGMA = 0.006f;            // short-rate volatility (per sqrt-year)
        private const float RATE_TERM_PREMIUM = 0.01f;      // baseline long-minus-short slope
        private const float RATE_CURVATURE = 0.0f;          // NS curvature factor
        private const float RATE_LAMBDA = 2.0f;             // NS decay (years)
        private const float CYCLE_PHASE_PER_PERIOD = 0.05f; // ~126 periods (~10.5 yr) per cycle

        private static readonly string[] ISSUE_NAMES = new string[]
        {
            "Emergency Note", "Municipal Note", "Water Revenue Bond",
            "Electric Revenue Bond", "Transit Revenue Bond",
            "Infrastructure Bond", "Capital Bond"
        };
        private static readonly float[] ISSUE_FACES = new float[]
        {
            25000f, 75000f, 150000f, 200000f, 300000f, 400000f, 750000f
        };
        private static readonly int[] ISSUE_PERIODS = new int[]
        {
            24, 36, 48, 60, 72, 84, 120
        };
        private static readonly RevenueSource[] ISSUE_REVENUE = new RevenueSource[]
        {
            RevenueSource.None, RevenueSource.None, RevenueSource.Water,
            RevenueSource.Electricity, RevenueSource.PublicTransport,
            RevenueSource.None, RevenueSource.None
        };

        // Issuer identities live in InitIssuersInternal() (Phase 5); the market
        // draws a face and term for each generated bond from these menus.
        private static readonly float[] MARKET_FACES = new float[] { 10000f, 25000f, 50000f, 75000f, 100000f, 250000f };
        private static readonly int[] MARKET_PERIODS = new int[] { 4, 6, 8, 10, 12, 16 };

        public EngineSnapshot Snapshot { get { return _snapshot; } }

        public float GrossIncome { get { return _grossIncome; } }
        public float TotalExpenses { get { return _totalExpenses; } }
        public float DebtBurden { get { return _debtBurden; } }
        public float DSCR { get { return _dscr; } }
        public float MonthsOfReserves { get { return _monthsOfReserves; } }
        // One-time Phase 2 migration banner: true after loading a pre-Phase-2 save;
        // the UI reads it once and calls AckCreditModelNotice() to clear it.
        public bool CreditModelNoticePending { get { return _creditModelNoticePending; } }
        public void AckCreditModelNotice() { _creditModelNoticePending = false; }
        public float NOI { get { return _noi; } }
        public CreditRating Rating { get { return _rating; } }
        public float BenchmarkRate { get { return _benchmarkRate; } }
        public float RequiredYield { get { return _requiredYield; } }
        public float PortfolioValue { get { return _portfolioValue; } }
        public int DefaultPenalty { get { return _defaultPenalty; } }
        public int TotalDefaults { get { return _totalDefaults; } }
        public float RealizedPL { get { return (float)_realizedPL; } }
        public int TicksInCurrentPeriod { get { return _tickCounter; } }

        public int IssuedCount { get { lock (_lock) { return _issuedBonds.Count; } } }
        public int MaxIssuedBonds { get { return MAX_ISSUED_BONDS; } }
        public int IssueTemplateCount { get { return ISSUE_NAMES.Length; } }

        public int PortfolioCount { get { lock (_lock) { return _portfolioBonds.Count; } } }
        public int MarketCount { get { lock (_lock) { return _marketBonds.Count; } } }

        public float RevenueVolatility { get { return _revenueVolatility; } }
        public float SwapPL { get { return (float)_swapPL; } }
        public int SwapCount { get { lock (_lock) { return _activeSwaps.Count; } } }
        public int MaxActiveSwaps { get { return MAX_ACTIVE_SWAPS; } }

        public float DemandScore { get { return _demandScore; } }
        public float DefaultProbability { get { return _defaultProbability; } }
        public float AbsorptionCapacity { get { return _absorptionCapacity; } }
        public float RemainingCapacity
        {
            get
            {
                lock (_lock)
                {
                    float currentFace = 0f;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                        currentFace += _issuedBonds[i].FaceValue;
                    float remaining = _absorptionCapacity - currentFace;
                    return remaining > 0f ? remaining : 0f;
                }
            }
        }
        public int Population { get { return _population; } }
        public float Happiness { get { return _happiness; } }
        public float EmploymentRate { get { return _employmentRate; } }
        public float PopulationGrowth { get { return _populationGrowth; } }
        public float CitizenConfidence { get { return _citizenConfidence; } }
        public float BondAppeal { get { return _bondAppeal; } }
        public float FinancialHealth { get { return _financialHealth; } }
        public string DemandLabelText { get { return CimDemandEngine.DemandLabel(_demandScore); } }
        public float CitizenBuyVolume { get { return _citizenBuyVolume; } }
        public float CitizenSellVolume { get { return _citizenSellVolume; } }
        public float MarketPressure { get { return _smoothedPressure; } }
        public string PressureLabelText { get { return CimDemandEngine.PressureLabel(_smoothedPressure); } }
        public float CitizenProceedsThisPeriod { get { return _citizenProceedsThisPeriod; } }
        public float TotalCitizenProceeds { get { return (float)_totalCitizenProceeds; } }
        public float Health { get { return _health; } }
        public float Education { get { return _education; } }
        public float LandValue { get { return _landValue; } }
        public float CrimeRate { get { return _crimeRate; } }
        public float CashReserves { get { return _cashReserves; } }
        public float CityVitals { get { return _cityVitals; } }
        public float Momentum { get { return CimDemandEngine.CalculateMomentumMultiplier(_currentMarketState, _previousMarketState, 1.5f); } }
        public int TransactionLogCount { get { return _transactionLog.Count; } }
        public int ReportCount { get { lock (_lock) { return _reportHistory.Count; } } }
        public int CurrentQuarter { get { return _quarterNumber; } }

        public float HazardMultiplier { get { return _hazardMultiplier; } set { _hazardMultiplier = value; } }
        public float RateVolatilityScale { get { return _rateVolatilityScale; } set { _rateVolatilityScale = value; } }
        public bool CitizenTradingEnabled { get { return _citizenTradingEnabled; } set { _citizenTradingEnabled = value; } }
        public bool RevenueBondsEnabled { get { return _revenueBondsEnabled; } set { _revenueBondsEnabled = value; } }

        public void GetReportSnapshot(List<QuarterlyReport> dest)
        {
            dest.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _reportHistory.Count; i++)
                    dest.Add(_reportHistory[i]);
            }
        }

        public void GetTransactionLogSnapshot(List<CimTransaction> dest)
        {
            dest.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _transactionLog.Count; i++)
                {
                    CimTransaction src = _transactionLog[i];
                    CimTransaction copy = new CimTransaction();
                    copy.Sequence = src.Sequence;
                    copy.BuyVolume = src.BuyVolume;
                    copy.SellVolume = src.SellVolume;
                    copy.Pressure = src.Pressure;
                    copy.Detail = src.Detail;
                    dest.Add(copy);
                }
            }
        }

        public float TotalHedgedNotional
        {
            get
            {
                lock (_lock)
                {
                    float total = 0f;
                    for (int i = 0; i < _activeSwaps.Count; i++)
                        total += _activeSwaps[i].NotionalAmount;
                    return total;
                }
            }
        }

        public float TotalDebtFace
        {
            get
            {
                lock (_lock)
                {
                    float total = 0f;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                        total += _issuedBonds[i].SubscribedFace;
                    return total;
                }
            }
        }

        public float OverHedgeRatio
        {
            get
            {
                lock (_lock)
                {
                    return CalculateOverHedgeRatioInternal();
                }
            }
        }

        public bool CanIssueBonds
        {
            get
            {
                lock (_lock)
                {
                    if (_issuedBonds.Count >= MAX_ISSUED_BONDS) return false;
                    if (_rating == CreditRating.D) return false;
                    if (_debtBook.IssuanceSuspended) return false;            // Schema v5: defaulted or in arrears
                    if (!_debtBook.RatingRecoveryAllowed(_periodCounter, DEFAULT_LOCKOUT_PERIODS)) return false; // lock-out window
                    if (_demandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND) return false;

                    if (_absorptionCapacity > 0f)
                    {
                        float currentFace = 0f;
                        for (int i = 0; i < _issuedBonds.Count; i++)
                            currentFace += _issuedBonds[i].FaceValue;
                        if (_absorptionCapacity - currentFace < 1000f) return false;
                    }

                    return true;
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
                        float remainingCoupons = (ib.SubscribedFace * ib.CouponRate / BondPricing.PeriodsPerYear) * ib.RemainingPeriods;
                        total += ib.SubscribedFace + remainingCoupons + ib.Arrears;
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
                        total += _issuedBonds[i].InterestPaid;
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

        public string GetTemplateName(int index) { return ISSUE_NAMES[index]; }
        public float GetTemplateFace(int index) { return ISSUE_FACES[index]; }
        public int GetTemplatePeriods(int index) { return ISSUE_PERIODS[index]; }
        public RevenueSource GetTemplateRevenue(int index) { return ISSUE_REVENUE[index]; }
        public bool IsTemplateAvailable(int index)
        {
            if (index < 0 || index >= ISSUE_REVENUE.Length) return false;
            if (ISSUE_REVENUE[index] != RevenueSource.None && !_revenueBondsEnabled) return false;
            return true;
        }
        public int AvailableTemplateCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < ISSUE_REVENUE.Length; i++)
                    if (IsTemplateAvailable(i)) n++;
                return n;
            }
        }

        public override long OnUpdateMoneyAmount(long internalMoneyAmount)
        {
            Instance = this;

            lock (_lock)
            {
                if (PendingSaveData != null)
                {
                    NeedsReset = false;
                    byte[] data = PendingSaveData;
                    PendingSaveData = null;
                    if (!RestoreState(data))
                        ResetStateInternal();
                }
                else if (NeedsReset)
                {
                    NeedsReset = false;
                    ResetStateInternal();
                }

                // P0-6: seed the cash cursor from the authoritative balance the
                // game just handed us. Every mod cash op this tick settles against
                // this cursor, not the stale LastCashAmount.
                _tickCash = internalMoneyAmount;

                UpdateCashFlowHistory(internalMoneyAmount);

                _tickCounter++;
                _ticksThisPeriod++;
                _totalTicks++;
                bool periodBoundary = false;
                try
                {
                    int month = Singleton<SimulationManager>.instance.m_currentGameTime.Month;
                    if (_lastGameMonth < 0) _lastGameMonth = month;
                    if (month != _lastGameMonth)
                    {
                        periodBoundary = true;
                        _lastGameMonth = month;
                        _ticksThisPeriod = 0;
                    }
                }
                catch
                {
                    periodBoundary = _tickCounter >= TICKS_PER_PERIOD;
                    if (periodBoundary) { _tickCounter = 0; _ticksThisPeriod = 0; }
                }

                // P2-2: the heavy metrics block (portfolio revaluation, credit model,
                // rate pipeline, demand chain, demographic sampling) runs ONCE per
                // period, not every tick - a ~15x reduction in per-tick engine work.
                // Run it once up front so the UI shows real numbers before the first
                // period elapses.
                if (periodBoundary || !_periodMetricsInitialized)
                {
                    _periodMetricsInitialized = true;
                    RecalculateMetricsInternal(internalMoneyAmount);
                }

                if (!_initialized)
                {
                    _initialized = true;
                    GenerateInitialBondsInternal();
                }

                if (_marketBonds.Count < MIN_MARKET_BONDS)
                {
                    RegenerateBondsInternal();
                }

                if (periodBoundary)
                {
                    AgeBondsInternal();
                }
            }

            return internalMoneyAmount;
        }

        private void UpdateCashFlowHistory(long internalMoneyAmount)
        {
            if (_prevMoneySet)
            {
                // P0-7: the raw balance change includes the cash the mod itself
                // moved since the last sample (coupons, maturities, placement
                // proceeds, swap settlements, UI buys/sells). Subtract the amounts
                // the game ACTUALLY moved so only organic revenue/expense lands in
                // the window. Using actual-moved (not requested) amounts makes this
                // self-correcting and removes the old _prevMoney fixups.
                long rawDelta = internalMoneyAmount - _prevMoney;
                long organic = rawDelta - _modCashDeltaPending;
                float delta = (float)organic;

                if (ShouldRecordDelta(delta))
                {
                    _cashFlowHistory[_windowIndex] = delta;
                    _windowIndex = (_windowIndex + 1) % WINDOW_SIZE;
                    if (_cashSamples < WINDOW_SIZE) _cashSamples++;
                }
                // else: a >6 sigma spike (usually a one-off game grant or a
                // desync). Re-baseline on the new balance rather than poison the
                // rolling statistics with it.
            }
            _prevMoney = internalMoneyAmount;
            _prevMoneySet = true;
            _modCashDeltaPending = 0; // consumed for this sample
        }

        // P0-7 sanity clamp: reject a delta that sits more than 6 standard
        // deviations off the rolling mean, but only once the window holds enough
        // samples to have a meaningful mean/stddev (otherwise everything looks
        // extreme and nothing would ever be recorded).
        private bool ShouldRecordDelta(float delta)
        {
            if (_cashSamples < WINDOW_SIZE) return true;

            float mean = 0f;
            for (int i = 0; i < WINDOW_SIZE; i++)
                mean += _cashFlowHistory[i];
            mean /= WINDOW_SIZE;

            float sumSq = 0f;
            for (int i = 0; i < WINDOW_SIZE; i++)
            {
                float d = _cashFlowHistory[i] - mean;
                sumSq += d * d;
            }
            float stddev = (float)Math.Sqrt(sumSq / WINDOW_SIZE);
            if (stddev <= 0f) return true;

            float z = Math.Abs(delta - mean) / stddev;
            return z <= 6f;
        }

        private void RecalculateMetricsInternal(long internalMoneyAmount)
        {
            float totalPositive = 0f;
            float totalNegative = 0f;

            for (int i = 0; i < WINDOW_SIZE; i++)
            {
                float v = _cashFlowHistory[i];
                if (v > 0f) totalPositive += v;
                else if (v < 0f) totalNegative += -v;
            }

            float cashDisplay = (float)internalMoneyAmount / INTERNAL_UNIT_SCALE;

            // WO-17: self-derive the sampling cadence so the annualization
            // factor tracks actual tick spacing, not a hardcoded constant.
            int elapsed = _totalTicks - _lastFlowSampleTick;
            if (elapsed > 0) _measuredTicksPerPeriod = elapsed;
            _lastFlowSampleTick = _totalTicks;

            // Source per-period operating flow from the game ledger when available;
            // otherwise fall back to the balance-delta proxy scaled by the measured
            // cadence. The sampler runs once per period, so its raw delta IS per-period.
            float periodIncome, periodExpense;
            bool flowFromLedger = SamplePeriodOperatingFlow(out periodIncome, out periodExpense);
            if (flowFromLedger)
            {
                float a = 0.25f; // ~4-period EMA horizon (per-period stepping)
                _avgIncomePerPeriod = _avgIncomePerPeriod <= 0f ? periodIncome : _avgIncomePerPeriod + a * (periodIncome - _avgIncomePerPeriod);
                _avgExpensePerPeriod = _avgExpensePerPeriod <= 0f ? periodExpense : _avgExpensePerPeriod + a * (periodExpense - _avgExpensePerPeriod);
            }
            else
            {
                _avgIncomePerPeriod = (totalPositive / WINDOW_SIZE) * _measuredTicksPerPeriod / INTERNAL_UNIT_SCALE;
                _avgExpensePerPeriod = (totalNegative / WINDOW_SIZE) * _measuredTicksPerPeriod / INTERNAL_UNIT_SCALE;
            }

            // RC-1: annualized credit metrics via the pure CreditModel.
            // Per-period averages × periodsPerYear — no tick factor.
            CreditMetrics cm = CreditModel.CalculateMetrics(
                _avgIncomePerPeriod, _avgExpensePerPeriod, _debtBook, cashDisplay,
                BondPricing.PeriodsPerYear);

            _grossIncome = cm.AnnualOperatingRevenue;
            _totalExpenses = cm.AnnualOperatingExpense;
            _noi = cm.AnnualNOI;
            _debtBurden = cm.DebtBurden;
            _dscr = cm.DSCR;
            _monthsOfReserves = cm.MonthsOfReserves;

            _portfolioValue = 0f;
            for (int i = 0; i < _portfolioBonds.Count; i++)
                _portfolioValue += BondPricing.PresentValue(_portfolioBonds[i], IssuerYieldFor(_portfolioBonds[i]));

            // Phase 2: calibrated rating grid + liquidity notch; hard D floor for
            // active arrears is handled inside RatingEngine.
            bool hasArrears = _debtBook.AnyDefaulted || _debtBook.TotalArrears > 0.01f;
            _rating = RatingEngine.EvaluateRating(cm, hasArrears);

            // Gate G-2: one-shot diagnostic to the Debug Output. Fires on the second
            // metrics pass (once the ledger baseline is set, so a real cumulative diff
            // is available), reporting whether the game ledger bound and the raw +
            // annualized numbers it produced - so "rating looks wrong" becomes a
            // definite read on whether EconomyReader is the cause rather than warm-up.
            // One line, once per load; VerboseLogging is not required for this check.
            _metricRuns++;
            if (_g2DiagSamples < 3 && (_metricRuns == 2 || _metricRuns == 10 || _metricRuns == 60))
            {
                _g2DiagSamples++;
                Debug.Log(string.Format(
                    "[MyFirstMod] G-2 sample {0}/3 (run {1}) | bound={2} shape={3} | flow source={4} | period inc/exp={5:F1}/{6:F1} (cadence={14}t) | annualized rev/exp/NOI={7:F0}/{8:F0}/{9:F0} | DSCR={10:F2} burden={11:F3} reserves={12:F1}mo | rating={13}",
                    _g2DiagSamples, _metricRuns,
                    EconomyReader.MethodResolved,
                    EconomyReader.BindingShape,
                    flowFromLedger ? "GAME LEDGER" : "balance-delta fallback",
                    periodIncome, periodExpense,
                    _grossIncome, _totalExpenses, _noi,
                    _dscr, _debtBurden, _monthsOfReserves,
                    BondPricing.RatingLabel(_rating),
                    _measuredTicksPerPeriod));
            }

            // Phase 4 (P1-4/P1-5): the exogenous short rate evolves once per period
            // via a mean-reverting process whose long-run mean drifts on a business
            // cycle. Nothing the city does moves it (P0-5). The Nelson-Siegel curve
            // (P1-3) is derived from the short rate for pricing and swaps.
            _cyclePhase = RateProcess.AdvancePhase(_cyclePhase, CYCLE_PHASE_PER_PERIOD);
            float theta = RateProcess.CycleTheta(RATE_BASE_THETA, RATE_CYCLE_AMPLITUDE, _cyclePhase);
            float z = RateProcess.NextGaussian(_rng);
            float dtYears = 1f / BondPricing.PeriodsPerYear;
            _shortRate = RateProcess.Step(_shortRate, RATE_KAPPA, theta, RATE_SIGMA * _rateVolatilityScale, dtYears, z);

            float longLevel = _shortRate + RATE_TERM_PREMIUM + _revenueVolatility * 0.01f;
            _yieldCurve = YieldCurve.FromShortRate(_shortRate, longLevel, RATE_CURVATURE, RATE_LAMBDA);
            _marketFloatingRate = _shortRate;
            _benchmarkRate = _marketFloatingRate;

            // City borrowing rate: the index plus the city's own fiscal adjustment
            // and over-hedge penalty. These price what the city ISSUES; they must
            // never leak into the swap settlement index, or over-hedging would lift
            // the floating leg that pays off the very swaps that caused it.
            float fiscalAdj = _debtBurden * 0.02f;
            _cityBorrowingRate = _marketFloatingRate + fiscalAdj;

            float overHedgeR = CalculateOverHedgeRatioInternal();
            if (overHedgeR > 0f)
            {
                float ohPenalty = overHedgeR * 0.015f;
                if (ohPenalty > 0.03f) ohPenalty = 0.03f;
                _cityBorrowingRate += ohPenalty;
            }

            float baseYield = BondPricing.GetRequiredYield(_cityBorrowingRate, _rating);
            float defaultSpike = _defaultPenalty * DEFAULT_YIELD_SPIKE_PER_POINT;
            _requiredYield = baseYield + defaultSpike;

            float totalWealth = cashDisplay + _portfolioValue;
            float periodsInWindow = (float)WINDOW_SIZE / _measuredTicksPerPeriod;
            float wealthBase = _avgIncomePerPeriod * periodsInWindow;
            if (wealthBase < 50000f) wealthBase = 50000f;
            float wealthRatio = totalWealth / wealthBase;
            if (wealthRatio < 0f) wealthRatio = 0f;
            if (wealthRatio > 4f) wealthRatio = 4f;
            float wealthAdj = 0.02f * (1f - wealthRatio);
            if (wealthAdj < -0.03f) wealthAdj = -0.03f;
            if (wealthAdj > 0.05f) wealthAdj = 0.05f;
            _requiredYield += wealthAdj;

            float avgPositiveFlow = totalPositive / WINDOW_SIZE;
            if (avgPositiveFlow > 0f)
            {
                float mean = 0f;
                for (int i = 0; i < WINDOW_SIZE; i++)
                    mean += _cashFlowHistory[i];
                mean /= WINDOW_SIZE;

                float sumSqDiff = 0f;
                for (int i = 0; i < WINDOW_SIZE; i++)
                {
                    float diff = _cashFlowHistory[i] - mean;
                    sumSqDiff += diff * diff;
                }
                float stddev = (float)Math.Sqrt(sumSqDiff / WINDOW_SIZE);
                _revenueVolatility = stddev / avgPositiveFlow;
                if (_revenueVolatility > 2f) _revenueVolatility = 2f;
            }
            else
            {
                _revenueVolatility = 0f;
            }

            ReadCityDemographicsInternal(cashDisplay);
            _cityVitals = CimDemandEngine.CalculateCityVitals(
                _population, _happiness, _health, _education, _landValue, _crimeRate);
            _financialHealth = CimDemandEngine.CalculateFinancialHealth(
                _cashReserves, _debtBurden, _dscr, _rating);
            _defaultProbability = CimDemandEngine.CalculateDefaultProbability(
                _debtBurden, _dscr, _defaultPenalty, _revenueVolatility);
            _citizenConfidence = CimDemandEngine.CalculateCitizenConfidence(
                _happiness, _employmentRate, _populationGrowth);
            _bondAppeal = CimDemandEngine.CalculateBondAppeal(
                _requiredYield, _benchmarkRate, _defaultProbability);

            _previousMarketState = _currentMarketState;
            _currentMarketState.CityVitals = _cityVitals;
            _currentMarketState.FinancialHealth = _financialHealth;
            _currentMarketState.CitizenConfidence = _citizenConfidence;
            _currentMarketState.BondAppeal = _bondAppeal;

            _demandScore = CimDemandEngine.CalculateDemandScore(
                _currentMarketState, _previousMarketState);
            _requiredYield = CimDemandEngine.AdjustYieldForDemand(_requiredYield, _demandScore);
            _requiredYield = CimDemandEngine.AdjustYieldForPressure(_requiredYield, _smoothedPressure);
            if (_requiredYield > 0.50f) _requiredYield = 0.50f;

            if (_prevRequiredYield > 0f)
            {
                // P1-5: this block now runs per period (P2-2), so cap the required
                // yield's move at 50bp/period - it trends visibly instead of
                // snapping, and the limiter actually binds.
                float maxDelta = 0.005f;
                float delta = _requiredYield - _prevRequiredYield;
                if (delta > maxDelta) _requiredYield = _prevRequiredYield + maxDelta;
                else if (delta < -maxDelta) _requiredYield = _prevRequiredYield - maxDelta;
            }
            _prevRequiredYield = _requiredYield;
            _absorptionCapacity = CimDemandEngine.CalculateAbsorptionCapacity(
                _population, _landValue, _education, _employmentRate, _demandScore);

            PublishSnapshot();
        }

        private float CalculateOverHedgeRatioInternal()
        {
            float totalDebtFace = 0f;
            for (int i = 0; i < _issuedBonds.Count; i++)
                totalDebtFace += _issuedBonds[i].SubscribedFace;

            float hedgedNotional = 0f;
            for (int i = 0; i < _activeSwaps.Count; i++)
                hedgedNotional += _activeSwaps[i].NotionalAmount;

            if (hedgedNotional <= totalDebtFace)
                return 0f;
            if (totalDebtFace <= 0f)
                return hedgedNotional > 0f ? 2f : 0f;
            return (hedgedNotional - totalDebtFace) / totalDebtFace;
        }

        private void PublishSnapshot()
        {
            var s = new EngineSnapshot();
            s.GrossIncome = _grossIncome;
            s.TotalExpenses = _totalExpenses;
            s.DebtBurden = _debtBurden;
            s.DSCR = _dscr;
            s.MonthsOfReserves = _monthsOfReserves;
            s.NOI = _noi;
            s.Rating = _rating;
            s.BenchmarkRate = _benchmarkRate;
            s.RequiredYield = _requiredYield;
            s.PortfolioValue = _portfolioValue;
            s.DefaultPenalty = _defaultPenalty;
            s.TotalDefaults = _totalDefaults;
            s.RealizedPL = (float)_realizedPL;
            s.TicksInCurrentPeriod = _tickCounter;
            s.IssuedCount = _issuedBonds.Count;
            s.PortfolioCount = _portfolioBonds.Count;
            s.MarketCount = _marketBonds.Count;
            s.RevenueVolatility = _revenueVolatility;
            s.SwapPL = (float)_swapPL;
            s.SwapCount = _activeSwaps.Count;
            s.DemandScore = _demandScore;
            s.DefaultProbability = _defaultProbability;
            s.AbsorptionCapacity = _absorptionCapacity;

            float currentFace = 0f;
            for (int i = 0; i < _issuedBonds.Count; i++)
                currentFace += _issuedBonds[i].FaceValue;
            float remCap = _absorptionCapacity - currentFace;
            s.RemainingCapacity = remCap > 0f ? remCap : 0f;

            s.Population = _population;
            s.Happiness = _happiness;
            s.EmploymentRate = _employmentRate;
            s.PopulationGrowth = _populationGrowth;
            s.CitizenConfidence = _citizenConfidence;
            s.BondAppeal = _bondAppeal;
            s.FinancialHealth = _financialHealth;
            s.CitizenBuyVolume = _citizenBuyVolume;
            s.CitizenSellVolume = _citizenSellVolume;
            s.SmoothedPressure = _smoothedPressure;
            s.CitizenProceedsThisPeriod = _citizenProceedsThisPeriod;
            s.TotalCitizenProceeds = (float)_totalCitizenProceeds;
            s.Health = _health;
            s.Education = _education;
            s.LandValue = _landValue;
            s.CrimeRate = _crimeRate;
            s.CashReserves = _cashReserves;
            s.CityVitals = _cityVitals;
            s.Momentum = CimDemandEngine.CalculateMomentumMultiplier(_currentMarketState, _previousMarketState, 1.5f);
            s.TransactionLogCount = _transactionLog.Count;
            s.ReportCount = _reportHistory.Count;
            s.CurrentQuarter = _quarterNumber;

            float debtFace = 0f;
            float debtOwed = 0f;
            float couponsPaidTotal = 0f;
            for (int i = 0; i < _issuedBonds.Count; i++)
            {
                Bond ib = _issuedBonds[i];
                debtFace += ib.SubscribedFace;
                float rc = (ib.SubscribedFace * ib.CouponRate / BondPricing.PeriodsPerYear) * ib.RemainingPeriods;
                debtOwed += ib.SubscribedFace + rc + ib.Arrears;
                couponsPaidTotal += ib.InterestPaid;
            }
            s.TotalDebtFace = debtFace;
            s.TotalDebtOwed = debtOwed;
            s.TotalCouponsPaid = couponsPaidTotal;

            float hedged = 0f;
            for (int i = 0; i < _activeSwaps.Count; i++)
                hedged += _activeSwaps[i].NotionalAmount;
            s.TotalHedgedNotional = hedged;
            s.OverHedgeRatio = CalculateOverHedgeRatioInternal();
            s.CreditStatusLabel = CreditStatusLabel;
            s.DemandLabelText = CimDemandEngine.DemandLabel(_demandScore);
            s.PressureLabelText = CimDemandEngine.PressureLabel(_smoothedPressure);

            _snapshot = s;
        }

        private void ReadCityDemographicsInternal(float cashDisplay)
        {
            _cashReserves = cashDisplay;

            bool gotPopulation = false;
            _gameDataAvailable = false;

            // P2-4: explicit null checks instead of exceptions as control flow.
            // Reading a valid manager's buffers does not throw; we skip cleanly
            // when the data isn't present yet.
            DistrictManager dm = Singleton<DistrictManager>.instance;
            if (dm != null && dm.m_districts.m_buffer != null && dm.m_districts.m_buffer.Length > 0)
            {
                District city = dm.m_districts.m_buffer[0];
                uint realPop = city.m_populationData.m_finalCount;
                if (realPop > 0)
                {
                    _population = (int)realPop;
                    gotPopulation = true;
                }
                _happiness = city.m_finalHappiness / 100f;
                _gameDataAvailable = true;
            }

            CitizenManager cm = Singleton<CitizenManager>.instance;
            if (cm != null && cm.m_citizens.m_buffer != null)
            {
                if (!gotPopulation && cm.m_citizenCount > 0)
                {
                    _population = cm.m_citizenCount;
                    gotPopulation = true;
                }
                _gameDataAvailable = true;

                // P2-2: this method now runs once per period, so sample every call
                // (previously gated to once per 15 ticks = once per period).
                // P2-4: one narrow try/catch around the raw buffer walk, logged if
                // it ever fires instead of silently fabricating demographics.
                try
                {
                    SampleCitizenDemographicsInternal(cm);
                }
                catch (Exception ex)
                {
                    Debug.Log("[MyFirstMod] Citizen sampling failed: " + ex.Message);
                }
            }

            if (!_gameDataAvailable)
            {
                // No usable game data yet (early load): synthesize plausible
                // demographics from fiscal state so the demand model stays sane.
                ApplyFallbackDemographicsInternal();
            }

            if (_happiness < 0f) _happiness = 0f;
            if (_happiness > 1f) _happiness = 1f;
            if (_health < 0f) _health = 0f;
            if (_health > 1f) _health = 1f;
            if (_education < 0f) _education = 0f;
            if (_education > 1f) _education = 1f;
            if (_landValue < 0f) _landValue = 0f;
            if (_landValue > 1f) _landValue = 1f;
            if (_crimeRate < 0f) _crimeRate = 0f;
            if (_crimeRate > 1f) _crimeRate = 1f;
            if (_employmentRate < 0.2f) _employmentRate = 0.2f;
            if (_employmentRate > 0.98f) _employmentRate = 0.98f;
            if (_population < 100) _population = 100;

            // P2-2: population growth is computed once per period (this method now
            // runs per period), matching the prior effective cadence.
            if (_prevPopulation > 0)
            {
                _populationGrowth = (float)(_population - _prevPopulation) / (float)_prevPopulation;
                if (_populationGrowth < -0.05f) _populationGrowth = -0.05f;
                if (_populationGrowth > 0.05f) _populationGrowth = 0.05f;
            }
            _prevPopulation = _population;
        }

        // P2-4: the raw citizen buffer walk, isolated so it is the only code under
        // a try/catch.
        private void SampleCitizenDemographicsInternal(CitizenManager cm)
        {
            float healthSum = 0f;
            float eduSum = 0f;
            float wellbeingSum = 0f;
            int sampled = 0;
            int employed = 0;
            uint bufSize = cm.m_citizens.m_size;
            int step = Math.Max(1, (int)(bufSize / 200));

            for (uint i = 0; i < bufSize && sampled < 200; i += (uint)step)
            {
                Citizen cit = cm.m_citizens.m_buffer[i];
                if ((cit.m_flags & Citizen.Flags.Created) != 0)
                {
                    healthSum += cit.m_health;
                    wellbeingSum += cit.m_wellbeing;
                    eduSum += (int)cit.EducationLevel;
                    if (cit.m_workBuilding != 0) employed++;
                    sampled++;
                }
            }

            if (sampled > 0)
            {
                _health = (healthSum / sampled) / 255f;
                _education = (eduSum / sampled) / 3f;
                float avgWellbeing = (wellbeingSum / sampled) / 255f;
                _landValue = avgWellbeing;
                _crimeRate = 1f - avgWellbeing;
                _employmentRate = (float)employed / sampled;
            }
        }

        // P2-4: fallback demographics synthesized from fiscal state, used only when
        // the game exposes no usable data (e.g. very early in a load).
        private void ApplyFallbackDemographicsInternal()
        {
            float avgIncome = _avgIncomePerPeriod;
            float avgExpense = _avgExpensePerPeriod;
            float dscrH = Math.Min(Math.Max(_dscr / 3f, 0f), 1f);

            if (_population < 100) _population = Math.Max(100, (int)(avgIncome / _measuredTicksPerPeriod * 10f));
            _happiness = dscrH;
            if (_health <= 0f) _health = dscrH * 0.8f + 0.2f;
            if (_education <= 0f) _education = 0.5f;
            if (_landValue <= 0f) _landValue = dscrH * 0.5f + 0.25f;
            _crimeRate = Math.Max(0f, 0.5f - dscrH * 0.4f);
            float totalFlow = avgIncome + avgExpense;
            _employmentRate = totalFlow > 0f ? avgIncome / totalFlow : 0.5f;
        }

        private float CalculateActiveDebtService()
        {
            return _debtBook.PeriodCouponTotal(BondPricing.PeriodsPerYear);
        }

        // WO-17: per-period operating flow from the game ledger. Called once per
        // period, so the raw delta between cumulative samples IS one period's
        // worth of flow. Returns false on the first sample (no baseline), on an
        // accumulator reset, or when the ledger API is unavailable.
        private bool SamplePeriodOperatingFlow(out float periodIncome, out float periodExpense)
        {
            periodIncome = 0f;
            periodExpense = 0f;

            long incCum, expCum;
            if (!EconomyReader.TryReadCumulative(out incCum, out expCum))
                return false;

            if (!_ledgerBaselineSet)
            {
                _prevLedgerIncome = incCum;
                _prevLedgerExpense = expCum;
                _ledgerBaselineSet = true;
                return false;
            }

            long di = incCum - _prevLedgerIncome;
            long de = expCum - _prevLedgerExpense;
            _prevLedgerIncome = incCum;
            _prevLedgerExpense = expCum;

            if (di < 0 || de < 0) return false; // accumulator rolled over / reset

            periodIncome = (float)di / INTERNAL_UNIT_SCALE;
            periodExpense = (float)de / INTERNAL_UNIT_SCALE;
            return true;
        }

        private void AgeBondsInternal()
        {
            _periodCounter++;

            for (int i = _portfolioBonds.Count - 1; i >= 0; i--)
            {
                Bond b = _portfolioBonds[i];
                b.RemainingPeriods--;

                if (b.RemainingPeriods <= 0)
                {
                    long faceInternal = (long)(b.FaceValue * INTERNAL_UNIT_SCALE);
                    AddCashToCity(faceInternal);
                    _realizedPL += (b.FaceValue + b.CouponsReceived) - b.PurchasePrice;
                    _portfolioBonds.RemoveAt(i);
                }
                else
                {
                    float couponPayment = (b.OutstandingPrincipal * b.CouponRate) / BondPricing.PeriodsPerYear;
                    long couponInternal = (long)(couponPayment * INTERNAL_UNIT_SCALE);
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
                {
                    _marketBonds.RemoveAt(i);
                }
            }

            ServiceIssuedBondsInternal();
            SettleSwapsInternal();
            if (_citizenTradingEnabled)
            {
                SimulateCitizenTradingInternal();
            }
            else
            {
                _citizenBuyVolume = 0f;
                _citizenSellVolume = 0f;
                _marketPressure = 0f;
                _pressureHistory[_pressureHistoryIndex] = 0f;
                _pressureHistoryIndex = (_pressureHistoryIndex + 1) % _pressureHistory.Length;
                float pSum = 0f;
                for (int i = 0; i < _pressureHistory.Length; i++) pSum += _pressureHistory[i];
                _smoothedPressure = pSum / _pressureHistory.Length;
            }
            MigrateIssuersAnnualInternal();

            // Schema v5: recovery is gated by the DebtBook. The default penalty
            // (and its yield spike) only fades once nothing is defaulted, arrears
            // are cleared, AND the lock-out window since the last default elapsed.
            if (_defaultPenalty > 0 &&
                _debtBook.RatingRecoveryAllowed(_periodCounter, DEFAULT_LOCKOUT_PERIODS))
            {
                _defaultPenalty = Math.Max(0, _defaultPenalty - DEFAULT_DECAY_PER_PERIOD);
            }

            _periodsSinceReport++;
            if (_periodsSinceReport >= PERIODS_PER_QUARTER)
            {
                _periodsSinceReport = 0;
                GenerateQuarterlyReportInternal();
            }

            PublishSnapshot();
        }

        private void ServiceIssuedBondsInternal()
        {
            float budget = (float)_tickCash / INTERNAL_UNIT_SCALE;
            int missed, newDefaults;
            float cashPaid = _debtBook.ServicePeriod(
                _periodCounter, budget, GRACE_PERIODS, ARREARS_SPREAD,
                DEFAULT_LOCKOUT_PERIODS, BondPricing.PeriodsPerYear,
                out missed, out newDefaults);

            if (cashPaid > 0f)
            {
                long wanted = (long)(cashPaid * INTERNAL_UNIT_SCALE);
                long actual = SpendCashUpTo(wanted);
                if (actual < wanted)
                    _debtBook.PushbackShortfall((float)(wanted - actual) / INTERNAL_UNIT_SCALE);
            }

            if (newDefaults > 0)
            {
                _defaultPenalty = Math.Min(
                    _defaultPenalty + newDefaults * DEFAULT_PENALTY_PER_EVENT, MAX_DEFAULT_PENALTY);
                _totalDefaults += newDefaults;
                _quarterDefaults += newDefaults;
            }
        }

        private void SettleSwapsInternal()
        {
            // P0-5: swaps settle against the exogenous index only.
            float floatingRate = _marketFloatingRate;

            for (int i = _activeSwaps.Count - 1; i >= 0; i--)
            {
                InterestRateSwap swap = _activeSwaps[i];
                swap.RemainingPeriods--;

                float netPayment;
                if (swap.PayFixed)
                    netPayment = (floatingRate - swap.FixedRate) * swap.NotionalAmount / BondPricing.PeriodsPerYear;
                else
                    netPayment = (swap.FixedRate - floatingRate) * swap.NotionalAmount / BondPricing.PeriodsPerYear;

                if (netPayment > 0f)
                {
                    long cashInternal = (long)(netPayment * INTERNAL_UNIT_SCALE);
                    if (cashInternal > 0)
                        AddCashToCity(cashInternal);
                }
                else if (netPayment < 0f)
                {
                    long cashInternal = (long)(-netPayment * INTERNAL_UNIT_SCALE);
                    if (cashInternal > 0 && !TrySpendCash(cashInternal))
                        swap.UnpaidSettlement += -netPayment;
                }

                swap.LastSettlement = netPayment;
                swap.CumulativePL += netPayment;
                _swapPL += netPayment;

                if (swap.RemainingPeriods <= 0)
                {
                    _activeSwaps.RemoveAt(i);
                }
            }
        }

        private void SimulateCitizenTradingInternal()
        {
            _citizenProceedsThisPeriod = 0f;

            if (_issuedBonds.Count == 0)
            {
                _citizenBuyVolume = 0f;
                _citizenSellVolume = 0f;
                _marketPressure = 0f;
                _pressureHistory[_pressureHistoryIndex] = 0f;
                _pressureHistoryIndex = (_pressureHistoryIndex + 1) % _pressureHistory.Length;
                float sum0 = 0f;
                for (int i = 0; i < _pressureHistory.Length; i++)
                    sum0 += _pressureHistory[i];
                _smoothedPressure = sum0 / _pressureHistory.Length;
                return;
            }

            CimDemandEngine.CalculateCitizenActivity(
                _population, _demandScore, _bondAppeal, _defaultProbability, _rng,
                out _citizenBuyVolume, out _citizenSellVolume);

            _marketPressure = CimDemandEngine.CalculateMarketPressure(
                _citizenBuyVolume, _citizenSellVolume);

            _pressureHistory[_pressureHistoryIndex] = _marketPressure;
            _pressureHistoryIndex = (_pressureHistoryIndex + 1) % _pressureHistory.Length;

            float sum = 0f;
            for (int i = 0; i < _pressureHistory.Length; i++)
                sum += _pressureHistory[i];
            _smoothedPressure = sum / _pressureHistory.Length;

            // Reuse a preallocated buffer rather than allocating each period on the
            // simulation thread (plan section 4). Issuance is capped at
            // MAX_ISSUED_BONDS, so the buffer always fits.
            int snapCount = _issuedBonds.Count;
            if (snapCount > _placementBefore.Length) snapCount = _placementBefore.Length;
            for (int i = 0; i < snapCount; i++)
                _placementBefore[i] = _issuedBonds[i].PlacedFraction;

            // Primary placement: citizen buying absorbs the still-unplaced part of
            // each issue. The DebtBook raises PlacedFraction and OutstandingPrincipal
            // together and returns the proceeds the city receives.
            if (_citizenBuyVolume > 0f)
            {
                float proceeds = _debtBook.PlacePrimary(_citizenBuyVolume);
                if (proceeds > 0f)
                {
                    // C-1: the city pays a 75bp underwriting fee on placed par,
                    // deducted from proceeds at closing.
                    float net = proceeds - Friction.UnderwritingFee(proceeds);
                    if (net < 0f) net = 0f;
                    AddCashToCity((long)(net * INTERNAL_UNIT_SCALE), EconomyManager.Resource.LoanAmount);
                    _citizenProceedsThisPeriod += net;
                    _totalCitizenProceeds += net;
                }
            }

            // P0-4: secondary-market selling is investor-to-investor. It does not
            // retire the city's debt and must never touch the treasury. Its only
            // effect is sell pressure, already captured in _marketPressure /
            // _smoothedPressure above and fed into the required yield. So there is
            // deliberately no sell-side cash or placement change here.

            // P2-3: reuse a StringBuilder instead of building the detail string by
            // repeated concatenation each period.
            float periodProceeds = _citizenProceedsThisPeriod;
            _detailBuilder.Length = 0;
            for (int i = 0; i < snapCount; i++)
            {
                Bond ib = _issuedBonds[i];
                float before = _placementBefore[i];
                float after = ib.PlacedFraction;
                float delta = after - before;
                if (delta > 0.001f || delta < -0.001f)
                {
                    if (_detailBuilder.Length > 0) _detailBuilder.Append("  ");
                    string arrow = delta > 0 ? ">" : "<";
                    _detailBuilder.Append(string.Format("{0}: {1:F0}%{2}{3:F0}%",
                        ib.Id, before * 100f, arrow, after * 100f));
                }
            }
            if (_detailBuilder.Length == 0)
            {
                _detailBuilder.Append(_citizenBuyVolume > _citizenSellVolume
                    ? "Fully subscribed" : "Sell pressure only");
            }
            if (periodProceeds > 0f)
                _detailBuilder.Append(string.Format("  +{0:N0} proceeds", periodProceeds));

            string detail = _detailBuilder.ToString();

            _transactionSeq++;
            CimTransaction tx = new CimTransaction();
            tx.Sequence = _transactionSeq;
            tx.BuyVolume = _citizenBuyVolume;
            tx.SellVolume = _citizenSellVolume;
            tx.Pressure = _marketPressure;
            tx.Detail = detail;
            _transactionLog.Add(tx);

            if (_transactionLog.Count > MAX_TRANSACTION_LOG)
                _transactionLog.RemoveAt(0);
        }

        private void GenerateQuarterlyReportInternal()
        {
            _quarterNumber++;

            QuarterlyReport rp = new QuarterlyReport();
            rp.Quarter = _quarterNumber;
            rp.Rating = _rating;
            rp.CreditStatus = CreditStatusLabel;
            rp.DSCR = _dscr;
            rp.DebtBurden = _debtBurden;
            rp.GrossIncome = _grossIncome;
            rp.TotalExpenses = _totalExpenses;
            rp.NOI = _noi;
            rp.DefaultProbability = _defaultProbability;
            rp.IssuedBonds = _issuedBonds.Count;
            rp.MaxBonds = MAX_ISSUED_BONDS;

            float debtFace = 0f;
            float debtOwed = 0f;
            float totalSub = 0f;
            float couponsPaid = 0f;
            for (int i = 0; i < _issuedBonds.Count; i++)
            {
                Bond ib = _issuedBonds[i];
                debtFace += ib.SubscribedFace;
                float rc = (ib.SubscribedFace * ib.CouponRate / BondPricing.PeriodsPerYear) * ib.RemainingPeriods;
                debtOwed += ib.SubscribedFace + rc + ib.Arrears;
                totalSub += ib.PlacedFraction;
                couponsPaid += ib.InterestPaid;
            }
            rp.DebtFace = debtFace;
            rp.DebtOwed = debtOwed;
            rp.AvgSubscription = _issuedBonds.Count > 0 ? totalSub / _issuedBonds.Count : 0f;
            rp.CouponsPaid = couponsPaid;
            rp.QuarterDefaults = _quarterDefaults;
            rp.TotalDefaults = _totalDefaults;
            rp.BenchmarkRate = _benchmarkRate;
            rp.RequiredYield = _requiredYield;
            rp.DemandScore = _demandScore;
            rp.SmoothedPressure = _smoothedPressure;
            rp.AbsorptionCapacity = _absorptionCapacity;
            rp.Population = _population;
            rp.PortfolioBonds = _portfolioBonds.Count;
            rp.SwapCount = _activeSwaps.Count;

            float hedged = 0f;
            for (int i = 0; i < _activeSwaps.Count; i++)
                hedged += _activeSwaps[i].NotionalAmount;
            rp.HedgedNotional = hedged;

            rp.RealizedPL = (float)_realizedPL;
            rp.SwapPL = (float)_swapPL;
            rp.RevenueVolatility = _revenueVolatility;
            rp.Happiness = _happiness;
            rp.EmploymentRate = _employmentRate;
            rp.PopulationGrowth = _populationGrowth;
            rp.CitizenConfidence = _citizenConfidence;
            rp.BondAppeal = _bondAppeal;
            rp.FinancialHealth = _financialHealth;
            rp.CitizenProceeds = (float)_totalCitizenProceeds;
            rp.Outlook = GenerateOutlookInternal();

            _reportHistory.Add(rp);
            if (_reportHistory.Count > MAX_REPORT_HISTORY)
                _reportHistory.RemoveAt(0);

            _quarterDefaults = 0;
        }

        private string GenerateOutlookInternal()
        {
            if (_rating == CreditRating.D)
                return "CRITICAL: City in default. Bond access suspended.";
            if (_rating == CreditRating.CCC)
                return "WARNING: Credit severely impaired. Fiscal action needed.";
            if (_dscr < 1.0f)
                return "CAUTION: Revenue insufficient for debt obligations.";
            if (_debtBurden > 0.30f)
                return "ELEVATED RISK: High debt burden straining finances.";
            if (_defaultPenalty > 6)
                return "DISTRESSED: Defaults weighing on yields and credit.";
            if (_defaultPenalty > 0)
                return "RECOVERING: Working through prior default penalties.";
            if (_demandScore >= 0.80f && _dscr > 2.0f)
                return "EXCELLENT: Strong finances, robust investor demand.";
            if (_demandScore >= 0.60f && _dscr > 1.5f)
                return "POSITIVE: Healthy fundamentals, good market access.";
            if (_demandScore >= 0.40f)
                return "STABLE: Adequate conditions for operations.";
            if (_demandScore >= 0.20f)
                return "MIXED: Weakening demand may limit issuance.";
            return "CHALLENGING: Low demand and weak fiscal position.";
        }

        private void GenerateInitialBondsInternal()
        {
            if (_issuers.Count == 0) InitIssuersInternal();
            _marketBonds.Clear();
            _marketBonds.Add(MakeBond("City Infrastructure Note", 10000f, 0.042f, 2));
            _marketBonds.Add(MakeBond("Transit Revenue Bond", 25000f, 0.047f, 4));
            _marketBonds.Add(MakeBond("Education Fund Bond", 50000f, 0.050f, 6));
            _marketBonds.Add(MakeBond("Water & Sewer Bond", 75000f, 0.053f, 8));
            _marketBonds.Add(MakeBond("General Obligation Bond", 100000f, 0.055f, 10));
            _marketBonds.Add(MakeBond("Capital Improvement Bond", 200000f, 0.058f, 12));
            for (int i = 0; i < _marketBonds.Count; i++)
            {
                AssignIssuer(_marketBonds[i], _rng);
                _marketBonds[i].CouponRate = IssuerYieldFor(_marketBonds[i]); // price near par at issuer credit
            }
        }

        private void RegenerateBondsInternal()
        {
            if (_issuers.Count == 0) InitIssuersInternal();
            while (_marketBonds.Count < MIN_MARKET_BONDS)
            {
                MarketIssuer m = _issuers[_rng.Next(_issuers.Count)];
                float face = MARKET_FACES[_rng.Next(MARKET_FACES.Length)];
                int term = MARKET_PERIODS[_rng.Next(MARKET_PERIODS.Length)];

                Bond b = MakeBond(m.Name, face, 0.05f, term);
                b.IssuerName = m.Name;
                b.IssuerRating = m.Rating;
                b.CouponRate = IssuerYieldFor(b); // P1-2: coupon reflects the ISSUER's credit
                _marketBonds.Add(b);
            }
        }

        // Phase 5 (P1-2): the six market issuers, each with its own migrating credit.
        private void InitIssuersInternal()
        {
            _issuers.Clear();
            _issuers.Add(IssuerModel.MakeIssuer("Regional Water District", IssuerArchetype.WaterDistrict));
            _issuers.Add(IssuerModel.MakeIssuer("Clean Power Grid", IssuerArchetype.PowerGrid));
            _issuers.Add(IssuerModel.MakeIssuer("State Transit Auth", IssuerArchetype.TransitAuthority));
            _issuers.Add(IssuerModel.MakeIssuer("Port Authority", IssuerArchetype.PortAuthority));
            _issuers.Add(IssuerModel.MakeIssuer("County Health System", IssuerArchetype.HealthSystem));
            _issuers.Add(IssuerModel.MakeIssuer("District School Board", IssuerArchetype.SchoolBoard));
        }

        private MarketIssuer FindIssuer(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _issuers.Count; i++)
                if (_issuers[i].Name == name) return _issuers[i];
            return null;
        }

        private void AssignIssuer(Bond b, Random rng)
        {
            if (_issuers.Count == 0) InitIssuersInternal();
            MarketIssuer m = _issuers[rng.Next(_issuers.Count)];
            b.IssuerName = m.Name;
            b.IssuerRating = m.Rating;
        }

        // P1-2: holdings price off the benchmark curve + the ISSUER's spread, never
        // the city's own credit-adjusted required yield.
        private float IssuerYieldFor(Bond b)
        {
            float spread = IssuerModel.IssuerSpread(b.IssuerRating);
            float years = (float)b.RemainingPeriods / BondPricing.PeriodsPerYear;
            float baseRate = _yieldCurve.Lambda > 0.0001f ? _yieldCurve.SpotRate(years) : _marketFloatingRate;
            float y = baseRate + spread;
            if (y < 0.005f) y = 0.005f;
            if (y > 0.60f) y = 0.60f;
            return y;
        }

        private float AuctionFairYield(int periods)
        {
            float years = (float)periods / BondPricing.PeriodsPerYear;
            float spot = _yieldCurve.Lambda > 0.0001f ? _yieldCurve.SpotRate(years) : _marketFloatingRate;
            return spot;
        }

        public float EstimateAuctionCover(int periods)
        {
            lock (_lock)
            {
                return PrimaryAuction.EstimateCover(_requiredYield, AuctionFairYield(periods), _demandScore);
            }
        }

        private float BondDurationYears(Bond b)
        {
            return (float)b.RemainingPeriods / BondPricing.PeriodsPerYear;
        }

        private static ItemClass.Service RevenueToService(RevenueSource src)
        {
            switch (src)
            {
                case RevenueSource.Water:          return ItemClass.Service.Water;
                case RevenueSource.Electricity:    return ItemClass.Service.Electricity;
                case RevenueSource.PublicTransport: return ItemClass.Service.PublicTransport;
                default: return ItemClass.Service.None;
            }
        }

        private float RevenueYieldAdjustment(RevenueSource src)
        {
            if (src == RevenueSource.None) return 0f;
            long svcIncome, svcExpense;
            if (!EconomyReader.TryReadService(RevenueToService(src), out svcIncome, out svcExpense))
                return 0f;
            float net = (float)(svcIncome - svcExpense);
            if (net > 0f) return -0.005f;
            if (net < 0f) return  0.01f;
            return 0f;
        }

        // P1-8: single-lot execution prices carry the bid-ask half-spread (impact is
        // reserved for the bulk paths). Buys pay the ask; sells receive the bid.
        private float BuyExecPrice(Bond b)
        {
            float mid = BondPricing.PresentValue(b, IssuerYieldFor(b));
            return mid * (1f + Friction.HalfSpread(b.IssuerRating, BondDurationYears(b)));
        }

        private float SellExecPrice(Bond b)
        {
            float mid = BondPricing.PresentValue(b, IssuerYieldFor(b));
            float px = mid * (1f - Friction.HalfSpread(b.IssuerRating, BondDurationYears(b)));
            return px < 0f ? 0f : px;
        }

        // P1-8/E-1: a bulk lot pays half-spread AND square-root price impact against
        // the issue's own depth, so a huge order is materially lossy. The Phase 6
        // ladder builder replaces the fixed 1B/10M buttons with a depth-aware ladder.
        private float BulkBuyExecPrice(Bond b)
        {
            float mid = BondPricing.PresentValue(b, IssuerYieldFor(b));
            float depth = Friction.DepthPerPeriod(b.FaceValue);
            return Friction.ExecutionPrice(mid, true, b.IssuerRating, BondDurationYears(b), mid, depth);
        }

        private float TotalMarketDepth()
        {
            float totalFace = 0f;
            for (int i = 0; i < _marketBonds.Count; i++)
                totalFace += _marketBonds[i].FaceValue;
            return totalFace * 100f;
        }

        private Bond MakeBond(string name, float face, float coupon, int periods)
        {
            _nextBondId++;
            return new Bond("B" + _nextBondId.ToString(), name, face, coupon, periods);
        }

        // P1-2 (A-2/A-3): once per in-game year, migrate every issuer's rating and
        // resolve any defaults. On default the holder receives the sector recovery
        // fraction of par (A-3) as a realised loss, the issuer's market and portfolio
        // paper is cleared, and the issuer is restructured back to its home rating so
        // the market keeps six live names.
        private void MigrateIssuersAnnualInternal()
        {
            _annualCounter++;
            if (_annualCounter < BondPricing.PeriodsPerYear) return;
            _annualCounter = 0;
            if (_issuers.Count == 0) return;

            for (int i = 0; i < _issuers.Count; i++)
            {
                MarketIssuer m = _issuers[i];
                bool defaulted;
                m.Rating = IssuerModel.MigrateAnnual(m.Rating, m.HomeRating, _hazardMultiplier, _rng, out defaulted);

                if (defaulted)
                {
                    ResolveIssuerDefaultInternal(m);
                    // Restructured entity re-enters at its home rating.
                    m.Rating = m.HomeRating;
                    m.Defaulted = false;
                }
            }

            // Propagate current issuer ratings onto outstanding market/portfolio paper.
            RefreshHoldingRatingsInternal();
        }

        private void ResolveIssuerDefaultInternal(MarketIssuer m)
        {
            float recovery = IssuerModel.RecoveryRateFor(m.Archetype);

            // Portfolio holdings of this issuer: pay recovery, realise the loss, drop.
            for (int i = _portfolioBonds.Count - 1; i >= 0; i--)
            {
                Bond b = _portfolioBonds[i];
                if (b.IssuerName != m.Name) continue;
                float payout = b.FaceValue * recovery;
                AddCashToCity((long)(payout * INTERNAL_UNIT_SCALE));
                _realizedPL += (payout + b.CouponsReceived) - b.PurchasePrice;
                _portfolioBonds.RemoveAt(i);
            }

            // Its market paper is pulled.
            for (int i = _marketBonds.Count - 1; i >= 0; i--)
                if (_marketBonds[i].IssuerName == m.Name)
                    _marketBonds.RemoveAt(i);

            // Log the credit event.
            _transactionSeq++;
            CimTransaction tx = new CimTransaction();
            tx.Sequence = _transactionSeq;
            tx.Detail = string.Format("DEFAULT: {0} - recovery {1:F0}% of par", m.Name, recovery * 100f);
            _transactionLog.Add(tx);
            if (_transactionLog.Count > MAX_TRANSACTION_LOG)
                _transactionLog.RemoveAt(0);
        }

        private void RefreshHoldingRatingsInternal()
        {
            for (int i = 0; i < _marketBonds.Count; i++)
            {
                MarketIssuer m = FindIssuer(_marketBonds[i].IssuerName);
                if (m != null) _marketBonds[i].IssuerRating = m.Rating;
            }
            for (int i = 0; i < _portfolioBonds.Count; i++)
            {
                MarketIssuer m = FindIssuer(_portfolioBonds[i].IssuerName);
                if (m != null) _portfolioBonds[i].IssuerRating = m.Rating;
            }
        }

        // P0-6: re-seed the cash cursor from the game at the start of a UI action,
        // which can fire seconds after the last tick. Within a tick the cursor is
        // already seeded from the authoritative balance, so this is only for the
        // out-of-tick UI entry points.
        private void SeedTickCashFromGame()
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            _tickCash = em != null ? em.LastCashAmount : 0L;
        }

        // P0-6/P0-7: spend up to `desired` from the treasury, never more than the
        // cursor says is available, and return the amount the game ACTUALLY moved.
        // Updates the cursor and the pending mod delta. This is the single spend
        // primitive; TrySpendCash is an all-or-nothing wrapper over it.
        private long SpendCashUpTo(long desired,
            EconomyManager.Resource resource = EconomyManager.Resource.LoanPayment)
        {
            if (desired <= 0) return 0L;
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return 0L;

            long want = desired;
            if (want > _tickCash) want = _tickCash;
            if (want <= 0L) return 0L;

            long moved = 0L;
            long remaining = want;
            while (remaining > 0L)
            {
                int chunk = (int)Math.Min(remaining, (long)int.MaxValue);
                int got = em.FetchResource(resource, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                moved += got;
                remaining -= chunk;
                if (got < chunk) break;
            }

            _tickCash -= moved;
            _modCashDeltaPending -= moved;
            return moved;
        }

        private bool TrySpendCash(long internalAmount)
        {
            if (internalAmount <= 0L) return true;
            if (_tickCash < internalAmount) return false;
            long actual = SpendCashUpTo(internalAmount);
            if (actual == internalAmount) return true;
            if (actual > 0L) AddCashToCity(actual);
            return false;
        }

        // Returns the amount the game actually added (P0-7). Callers that don't
        // care may ignore it.
        private long AddCashToCity(long internalAmount,
            EconomyManager.Resource resource = EconomyManager.Resource.PublicIncome)
        {
            if (internalAmount <= 0L) return 0L;
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return 0L;

            long added = 0L;
            long remaining = internalAmount;
            while (remaining > 0L)
            {
                int chunk = (int)Math.Min(remaining, (long)int.MaxValue);
                int got = em.AddResource(resource, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                added += got;
                remaining -= chunk;
                if (got < chunk) break;
            }

            _tickCash += added;
            _modCashDeltaPending += added;
            return added;
        }

        private void ResetStateInternal()
        {
            // P0-1 belt-and-braces: even though the mutual recursion with
            // RestoreState has been removed, refuse to re-enter so no future
            // caller can reintroduce a reset loop.
            if (_resetInProgress) return;
            _resetInProgress = true;
            try
            {
                ResetStateCore();
            }
            finally
            {
                _resetInProgress = false;
            }
        }

        private void ResetStateCore()
        {
            _marketBonds.Clear();
            _portfolioBonds.Clear();
            _debtBook.Clear();
            Array.Clear(_cashFlowHistory, 0, _cashFlowHistory.Length);

            _windowIndex = 0;
            _prevMoney = 0;
            _prevMoneySet = false;
            _cashSamples = 0;
            _tickCash = 0;
            _modCashDeltaPending = 0;
            _avgIncomePerPeriod = 0f;
            _avgExpensePerPeriod = 0f;
            _prevLedgerIncome = 0;
            _prevLedgerExpense = 0;
            _ledgerBaselineSet = false;
            _totalTicks = 0;
            _lastFlowSampleTick = 0;
            _measuredTicksPerPeriod = TICKS_PER_PERIOD;
            _monthsOfReserves = 0f;
            _creditModelNoticePending = false;
            EconomyReader.Reset();
            _rng = new DeterministicRandom(unchecked((int)DateTime.Now.Ticks));
            _cosmeticRng = new System.Random();
            _issuers.Clear();
            InitIssuersInternal();
            _annualCounter = 0;
            _tickCounter = 0;
            _ticksThisPeriod = 0;
            _lastGameMonth = -1;
            _periodCounter = 0;
            _periodMetricsInitialized = false;
            _g2DiagSamples = 0; _metricRuns = 0;
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
            _marketFloatingRate = 0f;
            _cityBorrowingRate = 0f;
            _shortRate = RATE_BASE_THETA;
            _cyclePhase = 0f;
            _yieldCurve = YieldCurve.FromShortRate(RATE_BASE_THETA, RATE_BASE_THETA + RATE_TERM_PREMIUM, RATE_CURVATURE, RATE_LAMBDA);
            _requiredYield = 0f;
            _portfolioValue = 0f;
            _prevRequiredYield = 0f;
            _activeSwaps.Clear();
            _nextSwapId = 0;
            _revenueVolatility = 0f;
            _swapPL = 0f;
            _hazardMultiplier = IssuerModel.HAZARD_STANDARD;
            _rateVolatilityScale = 1f;
            _citizenTradingEnabled = true;
            _revenueBondsEnabled = false;
            _demandScore = 0f;
            _defaultProbability = 0f;
            _cityVitals = 0f;
            _financialHealth = 0f;
            _citizenConfidence = 0f;
            _bondAppeal = 0f;
            _absorptionCapacity = 0f;
            _population = 0;
            _prevPopulation = 0;
            _happiness = 0.5f;
            _health = 0.5f;
            _education = 0.5f;
            _landValue = 0.5f;
            _crimeRate = 0.1f;
            _employmentRate = 0.7f;
            _populationGrowth = 0f;
            _cashReserves = 0f;
            _currentMarketState = new MarketState();
            _previousMarketState = new MarketState();
            _citizenBuyVolume = 0f;
            _citizenSellVolume = 0f;
            _marketPressure = 0f;
            _smoothedPressure = 0f;
            Array.Clear(_pressureHistory, 0, _pressureHistory.Length);
            _pressureHistoryIndex = 0;
            _citizenProceedsThisPeriod = 0f;
            _totalCitizenProceeds = 0f;
            _transactionLog.Clear();
            _transactionSeq = 0;
            _periodsSinceReport = 0;
            _quarterNumber = 0;
            _quarterDefaults = 0;
            _reportHistory.Clear();

            // P0-1: no RestoreState call here. Restore is orchestrated solely by
            // OnUpdateMoneyAmount so reset and restore can never call each other.
        }

        // P0-8: snapshots emit immutable BondView/SwapView copies, never live
        // references into simulation state.
        public void GetMarketSnapshot(List<BondView> outBonds, List<float> outPrices)
        {
            outBonds.Clear();
            outPrices.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _marketBonds.Count; i++)
                {
                    // P1-2: price off the issuer's credit, not the city's.
                    float price = BondPricing.PresentValue(_marketBonds[i], IssuerYieldFor(_marketBonds[i]));
                    outBonds.Add(BondView.From(_marketBonds[i], price));
                    outPrices.Add(price);
                }
            }
        }

        public void GetPortfolioSnapshot(List<BondView> outBonds, List<float> outPrices)
        {
            outBonds.Clear();
            outPrices.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _portfolioBonds.Count; i++)
                {
                    float price = BondPricing.PresentValue(_portfolioBonds[i], IssuerYieldFor(_portfolioBonds[i]));
                    outBonds.Add(BondView.From(_portfolioBonds[i], price));
                    outPrices.Add(price);
                }
            }
        }

        // P0-8: buy by stable Id. Returns false if the market bond is gone (aged
        // out / already bought) since the UI snapshot was taken.
        public bool BuyBond(string bondId)
        {
            if (string.IsNullOrEmpty(bondId)) return false;
            lock (_lock)
            {
                SeedTickCashFromGame();
                int marketIndex = IndexOfById(_marketBonds, bondId);
                if (marketIndex < 0)
                    return false;

                Bond bond = _marketBonds[marketIndex];
                float price = BuyExecPrice(bond); // P1-8: pay the ask (mid + half-spread)
                long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                if (!TrySpendCash(priceInternal))
                    return false;

                bond.PurchasePrice = price;
                _portfolioBonds.Add(bond);
                _marketBonds.RemoveAt(marketIndex);
                return true;
            }
        }

        // P0-8: sell by stable Id. Returns false if the holding is gone since the
        // UI snapshot was taken.
        public bool SellBond(string bondId)
        {
            if (string.IsNullOrEmpty(bondId)) return false;
            lock (_lock)
            {
                int portfolioIndex = IndexOfById(_portfolioBonds, bondId);
                if (portfolioIndex < 0)
                    return false;

                SeedTickCashFromGame();
                Bond bond = _portfolioBonds[portfolioIndex];
                float price = SellExecPrice(bond); // P1-8: receive the bid (mid - half-spread)
                long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                AddCashToCity(priceInternal);
                _realizedPL += (price + bond.CouponsReceived) - bond.PurchasePrice;
                _portfolioBonds.RemoveAt(portfolioIndex);
                return true;
            }
        }

        public int SellAllBonds()
        {
            lock (_lock)
            {
                int count = _portfolioBonds.Count;
                for (int i = _portfolioBonds.Count - 1; i >= 0; i--)
                {
                    Bond bond = _portfolioBonds[i];
                    float price = SellExecPrice(bond);
                    long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);
                    AddCashToCity(priceInternal);
                    _realizedPL += (price + bond.CouponsReceived) - bond.PurchasePrice;
                    _portfolioBonds.RemoveAt(i);
                }
                return count;
            }
        }

        public bool IssueBond(int optionIndex)
        {
            lock (_lock)
            {
                if (optionIndex < 0 || optionIndex >= ISSUE_NAMES.Length)
                    return false;
                if (_issuedBonds.Count >= MAX_ISSUED_BONDS)
                    return false;
                if (_rating == CreditRating.D)
                    return false;
                if (_debtBook.IssuanceSuspended ||
                    !_debtBook.RatingRecoveryAllowed(_periodCounter, DEFAULT_LOCKOUT_PERIODS)) // Schema v5 lock-out
                    return false;
                if (_demandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND)
                    return false;

                SeedTickCashFromGame();

                string name = ISSUE_NAMES[optionIndex];
                float face = ISSUE_FACES[optionIndex];
                int periods = ISSUE_PERIODS[optionIndex];
                RevenueSource revSrc = ISSUE_REVENUE[optionIndex];

                if (revSrc != RevenueSource.None && !_revenueBondsEnabled)
                    return false;

                float currentFace = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    currentFace += _issuedBonds[i].FaceValue;
                if (currentFace + face > _absorptionCapacity)
                    return false;

                float offeredYield = _requiredYield + RevenueYieldAdjustment(revSrc);
                float fairYield = AuctionFairYield(periods);
                AuctionResult ar = PrimaryAuction.Evaluate(offeredYield, fairYield, _demandScore);
                if (!ar.Filled)
                    return false;

                float couponRate = ar.ClearingYield;

                _nextBondId++;
                Bond ib = new Bond("IB" + _nextBondId.ToString(), name, face, couponRate, periods);
                ib.PlacedFraction = ar.FilledFraction;
                ib.OutstandingPrincipal = face * ar.FilledFraction;
                ib.IssuePeriod = _periodCounter;
                ib.Revenue = revSrc;
                _debtBook.Add(ib);

                if (ib.OutstandingPrincipal > 0f)
                {
                    float proceeds = ib.OutstandingPrincipal - Friction.UnderwritingFee(ib.OutstandingPrincipal);
                    if (proceeds > 0f)
                        AddCashToCity((long)(proceeds * INTERNAL_UNIT_SCALE), EconomyManager.Resource.LoanAmount);
                }
                return true;
            }
        }

        public bool IssueBondPercent(float percent)
        {
            lock (_lock)
            {
                if (_issuedBonds.Count >= MAX_ISSUED_BONDS)
                    return false;
                if (_rating == CreditRating.D)
                    return false;
                if (_debtBook.IssuanceSuspended ||
                    !_debtBook.RatingRecoveryAllowed(_periodCounter, DEFAULT_LOCKOUT_PERIODS))
                    return false;
                if (_demandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND)
                    return false;

                SeedTickCashFromGame();

                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return false;

                float bankBalance = (float)em.LastCashAmount / INTERNAL_UNIT_SCALE;
                if (bankBalance <= 0f) return false;

                float face = bankBalance * percent;
                if (face < 1000f) face = 1000f;

                float currentFace = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    currentFace += _issuedBonds[i].FaceValue;

                float remainingCapacity = _absorptionCapacity - currentFace;
                if (remainingCapacity < 1000f)
                    return false;
                if (face > remainingCapacity)
                    face = remainingCapacity;

                int periods = 60;
                float offeredYield = _requiredYield;
                float fairYield = AuctionFairYield(periods);
                AuctionResult ar = PrimaryAuction.Evaluate(offeredYield, fairYield, _demandScore);
                if (!ar.Filled)
                    return false;

                float couponRate = ar.ClearingYield;

                _nextBondId++;
                string name = string.Format("{0:F0}% Bank Bond", percent * 100f);
                Bond ib = new Bond("IB" + _nextBondId.ToString(), name, face, couponRate, periods);
                ib.PlacedFraction = ar.FilledFraction;
                ib.OutstandingPrincipal = face * ar.FilledFraction;
                ib.IssuePeriod = _periodCounter;
                _debtBook.Add(ib);

                if (ib.OutstandingPrincipal > 0f)
                {
                    float proceeds = ib.OutstandingPrincipal - Friction.UnderwritingFee(ib.OutstandingPrincipal);
                    if (proceeds > 0f)
                        AddCashToCity((long)(proceeds * INTERNAL_UNIT_SCALE), EconomyManager.Resource.LoanAmount);
                }
                return true;
            }
        }

        public PayDebtResult PayDebtPercent(float percent)
        {
            lock (_lock)
            {
                var result = new PayDebtResult();
                if (_debtBook.Count == 0)
                    return result;

                SeedTickCashFromGame();

                float available = (float)_tickCash / INTERNAL_UNIT_SCALE;
                float budget = _debtBook.TotalDebtOwed * percent;
                if (budget > available) budget = available;

                int retired;
                bool partial;
                float spent = _debtBook.RepayByBudget(
                    _periodCounter, budget, DEFAULT_LOCKOUT_PERIODS, out retired, out partial);

                if (spent > 0f)
                {
                    long wanted = (long)(spent * INTERNAL_UNIT_SCALE);
                    long actual = SpendCashUpTo(wanted);
                    if (actual < wanted)
                        _debtBook.PushbackShortfall((float)(wanted - actual) / INTERNAL_UNIT_SCALE);
                    spent = (float)actual / INTERNAL_UNIT_SCALE;
                }

                result.Retired = retired;
                result.PartialPaydown = partial && retired == 0;
                result.AmountSpent = spent;
                return result;
            }
        }

        public bool Buy1BBond()
        {
            lock (_lock)
            {
                SeedTickCashFromGame();
                float face = 1000000000f;
                if (face > TotalMarketDepth()) return false;
                int periods = 60;

                Bond b = MakeBond("Institutional Sovereign Note", face, 0.05f, periods);
                AssignIssuer(b, _cosmeticRng);
                float price = BulkBuyExecPrice(b);
                long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                if (!TrySpendCash(priceInternal))
                    return false;

                b.PurchasePrice = price;
                _portfolioBonds.Add(b);
                return true;
            }
        }

        public int Buy10x1MBonds()
        {
            lock (_lock)
            {
                SeedTickCashFromGame();
                float depthRemaining = TotalMarketDepth();
                int bought = 0;

                for (int i = 0; i < 10; i++)
                {
                    float face = 1000000f;
                    if (face > depthRemaining) break;

                    Bond b = MakeBond("Corporate Tranche Note", face, 0.05f, 60);
                    AssignIssuer(b, _cosmeticRng);
                    float price = BulkBuyExecPrice(b);
                    long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                    if (!TrySpendCash(priceInternal)) break;

                    depthRemaining -= face;
                    b.PurchasePrice = price;
                    _portfolioBonds.Add(b);
                    bought++;
                }
                return bought;
            }
        }

        public int Buy10x10MBonds()
        {
            lock (_lock)
            {
                SeedTickCashFromGame();
                float depthRemaining = TotalMarketDepth();
                int bought = 0;

                for (int i = 0; i < 10; i++)
                {
                    float face = 10000000f;
                    if (face > depthRemaining) break;

                    Bond b = MakeBond("10M Treasury Bond", face, 0.05f, 60);
                    AssignIssuer(b, _cosmeticRng);
                    float price = BulkBuyExecPrice(b);
                    long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                    if (!TrySpendCash(priceInternal)) break;

                    depthRemaining -= face;
                    b.PurchasePrice = price;
                    _portfolioBonds.Add(b);
                    bought++;
                }
                return bought;
            }
        }

        public bool EnterSwap(float notional, float fixedRate, int periods, bool payFixed)
        {
            lock (_lock)
            {
                if (_activeSwaps.Count >= MAX_ACTIVE_SWAPS)
                    return false;
                for (int j = 0; j < _activeSwaps.Count; j++)
                    if (_activeSwaps[j].UnpaidSettlement > 0f) return false;
                if (notional <= 0f || periods <= 0)
                    return false;

                _nextSwapId++;
                InterestRateSwap swap = new InterestRateSwap(
                    "SW" + _nextSwapId.ToString(), notional, fixedRate, periods, payFixed);
                _activeSwaps.Add(swap);
                return true;
            }
        }

        private float CalculateSwapMTM(InterestRateSwap swap)
        {
            // P1-6: mark to market off the term structure (PV float - PV fixed),
            // not the old linear (floating - fixed) x years approximation.
            return SwapPricing.SwapValue(_yieldCurve, swap.NotionalAmount, swap.FixedRate,
                swap.RemainingPeriods, BondPricing.PeriodsPerYear, swap.PayFixed);
        }

        private bool SettleSwapCash(float mtmValue)
        {
            if (mtmValue > 0f)
            {
                long cashInternal = (long)(mtmValue * INTERNAL_UNIT_SCALE);
                if (cashInternal > 0)
                    AddCashToCity(cashInternal);
            }
            else if (mtmValue < 0f)
            {
                long cashInternal = (long)(-mtmValue * INTERNAL_UNIT_SCALE);
                if (cashInternal > 0 && !TrySpendCash(cashInternal))
                    return false;
            }
            _swapPL += mtmValue;
            return true;
        }

        // P0-8: terminate by stable Id. Returns false if the swap is gone
        // (matured/removed) since the UI snapshot was taken.
        public bool TerminateSwap(string swapId)
        {
            if (string.IsNullOrEmpty(swapId)) return false;
            lock (_lock)
            {
                int index = IndexOfSwapById(_activeSwaps, swapId);
                if (index < 0)
                    return false;

                SeedTickCashFromGame();
                float mtm = CalculateSwapMTM(_activeSwaps[index]);
                if (!SettleSwapCash(mtm))
                    return false;

                _activeSwaps.RemoveAt(index);
                return true;
            }
        }

        public int TerminateAllSwaps()
        {
            lock (_lock)
            {
                SeedTickCashFromGame();
                int count = 0;
                for (int i = _activeSwaps.Count - 1; i >= 0; i--)
                {
                    float mtm = CalculateSwapMTM(_activeSwaps[i]);
                    if (SettleSwapCash(mtm))
                    {
                        _activeSwaps.RemoveAt(i);
                        count++;
                    }
                }
                return count;
            }
        }

        public bool SellSwapTranche(string swapId, float fraction)
        {
            lock (_lock)
            {
                int index = -1;
                for (int i = 0; i < _activeSwaps.Count; i++)
                {
                    if (_activeSwaps[i].Id == swapId) { index = i; break; }
                }
                if (index < 0)
                    return false;
                if (fraction <= 0f || fraction > 1f)
                    return false;

                SeedTickCashFromGame();
                InterestRateSwap swap = _activeSwaps[index];
                float fullMTM = CalculateSwapMTM(swap);
                float settleMTM = fullMTM * fraction;

                if (fraction >= 1f || swap.NotionalAmount * (1f - fraction) < 1000f)
                {
                    if (!SettleSwapCash(fullMTM))
                        return false;
                    _activeSwaps.RemoveAt(index);
                    return true;
                }

                if (!SettleSwapCash(settleMTM))
                    return false;

                swap.NotionalAmount *= (1f - fraction);
                return true;
            }
        }

        public int SellAllSwapsTranche(float fraction)
        {
            lock (_lock)
            {
                if (fraction <= 0f || fraction > 1f)
                    return 0;

                SeedTickCashFromGame();
                int affected = 0;
                for (int i = _activeSwaps.Count - 1; i >= 0; i--)
                {
                    InterestRateSwap swap = _activeSwaps[i];
                    float fullMTM = CalculateSwapMTM(swap);

                    if (fraction >= 1f || swap.NotionalAmount * (1f - fraction) < 1000f)
                    {
                        if (!SettleSwapCash(fullMTM))
                            continue;
                        _activeSwaps.RemoveAt(i);
                    }
                    else
                    {
                        float settleMTM = fullMTM * fraction;
                        if (!SettleSwapCash(settleMTM))
                            continue;
                        // P1-6: scale notional only; realised P/L is immutable.
                        swap.NotionalAmount *= (1f - fraction);
                    }
                    affected++;
                }
                return affected;
            }
        }

        public bool AutoHedge()
        {
            lock (_lock)
            {
                if (_activeSwaps.Count >= MAX_ACTIVE_SWAPS)
                    return false;
                for (int j = 0; j < _activeSwaps.Count; j++)
                    if (_activeSwaps[j].UnpaidSettlement > 0f) return false;
                if (_issuedBonds.Count == 0)
                    return false;

                float totalDebtFace = 0f;
                float weightedPeriods = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                {
                    totalDebtFace += _issuedBonds[i].SubscribedFace;
                    weightedPeriods += _issuedBonds[i].SubscribedFace * _issuedBonds[i].RemainingPeriods;
                }

                float hedgedNotional = 0f;
                for (int i = 0; i < _activeSwaps.Count; i++)
                    hedgedNotional += _activeSwaps[i].NotionalAmount;

                float unhedged = totalDebtFace - hedgedNotional;
                if (unhedged <= 0f)
                    return false;

                int avgPeriods = totalDebtFace > 0f
                    ? (int)(weightedPeriods / totalDebtFace)
                    : 60;
                if (avgPeriods < 6) avgPeriods = 6;

                // P1-6: a swap's fixed leg is the par swap rate off the risk-free
                // curve, not the city's credit-adjusted borrowing yield.
                float parRate = SwapPricing.ParSwapRate(_yieldCurve, avgPeriods, BondPricing.PeriodsPerYear);

                _nextSwapId++;
                InterestRateSwap swap = new InterestRateSwap(
                    "SW" + _nextSwapId.ToString(), unhedged, parRate, avgPeriods, true);
                _activeSwaps.Add(swap);
                return true;
            }
        }

        public string CalculateRecommendedHedge()
        {
            lock (_lock)
            {
                float totalDebtFace = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    totalDebtFace += _issuedBonds[i].SubscribedFace;

                float hedgedNotional = 0f;
                for (int i = 0; i < _activeSwaps.Count; i++)
                    hedgedNotional += _activeSwaps[i].NotionalAmount;

                float overHedgeR = CalculateOverHedgeRatioInternal();
                if (overHedgeR > 0f)
                {
                    float penalty = overHedgeR * 4f;
                    if (penalty > 10f) penalty = 10f;
                    return string.Format("OVER-HEDGED {0:F0}%  Rate +{1:F1}%", overHedgeR * 100f, penalty);
                }

                if (_issuedBonds.Count == 0 && _activeSwaps.Count == 0)
                    return "No debt to hedge";

                float unhedged = totalDebtFace - hedgedNotional;
                float hedgeRatio = totalDebtFace > 0f ? hedgedNotional / totalDebtFace : 0f;

                if (hedgeRatio >= 1.0f)
                    return "Fully hedged";
                if (_revenueVolatility > 0.5f && hedgeRatio < 0.5f)
                    return string.Format("HIGH RISK: Hedge {0:N0} ({1:F0}% exposed)", unhedged, (1f - hedgeRatio) * 100f);
                if (unhedged > 0f)
                    return string.Format("Recommend: Hedge {0:N0} unhedged", unhedged);
                return "Position balanced";
            }
        }

        public void GetActiveSwapsSnapshot(List<SwapView> outSwaps)
        {
            outSwaps.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _activeSwaps.Count; i++)
                    outSwaps.Add(SwapView.From(_activeSwaps[i]));
            }
        }

        public void GetIssuedBondsSnapshot(List<BondView> outBonds)
        {
            outBonds.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _issuedBonds.Count; i++)
                    outBonds.Add(BondView.From(_issuedBonds[i], 0f));
            }
        }

        // P0-8: retire by stable Id. Returns false if the bond is already gone
        // (matured/removed) since the UI snapshot was taken.
        public bool RepaySingleBond(string bondId)
        {
            if (string.IsNullOrEmpty(bondId)) return false;
            lock (_lock)
            {
                Bond ib = _debtBook.FindActive(bondId);
                if (ib == null) return false;

                SeedTickCashFromGame();
                // P0-2: retiring a bond must clear its outstanding principal AND
                // any default arrears, or a defaulted bond (principal already
                // rolled into arrears) could be removed for free.
                long owedInternal = (long)((ib.OutstandingPrincipal + ib.Arrears) * INTERNAL_UNIT_SCALE);
                if (!TrySpendCash(owedInternal))
                    return false;

                _debtBook.RemoveBond(bondId);
                return true;
            }
        }

        private static int IndexOfById(List<Bond> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return i;
            return -1;
        }

        private static int IndexOfSwapById(List<InterestRateSwap> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return i;
            return -1;
        }

        public byte[] SerializeState()
        {
            lock (_lock)
            {
                try
                {
                    // Schema v5 (spec section 6): pack fields into a pure snapshot
                    // and let StateSerializer produce the sectioned, checksummed v7
                    // stream.
                    BondMarketState state = CaptureState();
                    return StateSerializer.Serialize(state);
                }
                catch (Exception ex)
                {
                    Debug.Log("[MyFirstMod] SerializeState failed: " + ex.Message);
                    return null;
                }
            }
        }

        private bool _restoring; // Schema v5 (spec section 6): restore re-entrancy guard

        // Delegates to the pure, staged, checksummed StateSerializer. Returns true
        // on success; false on any failure (corrupt data, bad checksum, unknown
        // version, failed invariants), in which case the caller resets to a clean
        // state. A restore already in flight is a no-op (re-entrancy guard). This
        // method never calls ResetStateInternal itself.
        private bool RestoreState(byte[] data)
        {
            if (_restoring) return false;
            _restoring = true;
            try
            {
                BondMarketState s;
                if (!StateSerializer.TryDeserialize(data, out s))
                {
                    Debug.Log("[MyFirstMod] RestoreState: save invalid/corrupt - caller will reset to fresh state.");
                    return false;
                }

                ApplyState(s);
                Debug.Log("[MyFirstMod] RestoreState: OK. Bonds P/I/M=" +
                    _portfolioBonds.Count + "/" + _issuedBonds.Count + "/" + _marketBonds.Count +
                    " Swaps=" + _activeSwaps.Count + " Reports=" + _reportHistory.Count);
                // Phase 2 migration notice: existing saves now score under the
                // annualized credit model (true DSCR + operating cash flows), so
                // ratings may shift from what this save last displayed.
                Debug.Log("[MyFirstMod] Credit Model Upgraded: Municipal bond ratings are now " +
                    "calculated using annualized debt service coverage (DSCR) and true operating " +
                    "cash flows. Existing city ratings may adjust accordingly.");
                _creditModelNoticePending = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("[MyFirstMod] RestoreState failed: " + ex.Message + " - caller will reset to fresh state.");
                return false;
            }
            finally
            {
                _restoring = false;
            }
        }

        // Pack engine state into the pure snapshot for serialization.
        private BondMarketState CaptureState()
        {
            BondMarketState s = new BondMarketState();
            s.NextBondId = _nextBondId; s.NextSwapId = _nextSwapId; s.TickCounter = _tickCounter;
            s.PeriodCounter = _periodCounter; s.DefaultPenalty = _defaultPenalty; s.TotalDefaults = _totalDefaults;
            s.RealizedPL = (float)_realizedPL; s.SwapPL = (float)_swapPL; s.WindowIndex = _windowIndex;
            s.Initialized = _initialized; s.TransactionSeq = _transactionSeq; s.PressureHistoryIndex = _pressureHistoryIndex;
            s.PeriodsSinceReport = _periodsSinceReport; s.QuarterNumber = _quarterNumber; s.QuarterDefaults = _quarterDefaults;
            s.TotalCitizenProceeds = (float)_totalCitizenProceeds; s.LastDefaultPeriod = _debtBook.LastDefaultPeriod;
            s.ShortRate = _shortRate; s.CyclePhase = _cyclePhase;

            s.CashFlowHistory = (float[])_cashFlowHistory.Clone();
            s.PressureHistory = (float[])_pressureHistory.Clone();

            s.Portfolio = new List<Bond>(_portfolioBonds);
            s.Issued = new List<Bond>(_debtBook.Bonds);
            s.Redeemed = new List<Bond>(_debtBook.Redeemed);
            s.Market = new List<Bond>(_marketBonds);
            s.Swaps = new List<InterestRateSwap>(_activeSwaps);
            s.Transactions = new List<CimTransaction>(_transactionLog);
            s.Reports = new List<QuarterlyReport>(_reportHistory);
            s.Issuers = new List<MarketIssuer>(_issuers);      // Phase 5 (P1-2)
            s.RngState = _rng.GetState();                       // Phase 5 (G-1)
            s.HazardMultiplier = _hazardMultiplier;
            s.RateVolatilityScale = _rateVolatilityScale;
            s.CitizenTradingEnabled = _citizenTradingEnabled;
            s.RevenueBondsEnabled = _revenueBondsEnabled;
            return s;
        }

        // Apply a validated snapshot back into engine state.
        private void ApplyState(BondMarketState s)
        {
            _nextBondId = s.NextBondId; _nextSwapId = s.NextSwapId; _tickCounter = s.TickCounter;
            _periodCounter = s.PeriodCounter; _defaultPenalty = s.DefaultPenalty; _totalDefaults = s.TotalDefaults;
            _realizedPL = s.RealizedPL; _swapPL = s.SwapPL; _windowIndex = s.WindowIndex;
            _initialized = s.Initialized; _transactionSeq = s.TransactionSeq; _pressureHistoryIndex = s.PressureHistoryIndex;
            _periodsSinceReport = s.PeriodsSinceReport; _quarterNumber = s.QuarterNumber; _quarterDefaults = s.QuarterDefaults;
            _totalCitizenProceeds = s.TotalCitizenProceeds;
            _shortRate = s.ShortRate;
            _cyclePhase = s.CyclePhase;
            _yieldCurve = YieldCurve.FromShortRate(_shortRate, _shortRate + RATE_TERM_PREMIUM, RATE_CURVATURE, RATE_LAMBDA);

            CopyInto(_cashFlowHistory, s.CashFlowHistory);
            CopyInto(_pressureHistory, s.PressureHistory);
            if (_windowIndex < 0 || _windowIndex >= WINDOW_SIZE) _windowIndex = 0;
            if (_pressureHistory.Length == 0 || _pressureHistoryIndex < 0 || _pressureHistoryIndex >= _pressureHistory.Length)
                _pressureHistoryIndex = 0;

            _portfolioBonds.Clear(); _portfolioBonds.AddRange(s.Portfolio);
            _marketBonds.Clear(); _marketBonds.AddRange(s.Market);
            _activeSwaps.Clear(); _activeSwaps.AddRange(s.Swaps);
            _transactionLog.Clear(); _transactionLog.AddRange(s.Transactions);
            _reportHistory.Clear(); _reportHistory.AddRange(s.Reports);

            _debtBook.Clear();
            for (int i = 0; i < s.Issued.Count; i++) _debtBook.Add(s.Issued[i]);
            for (int i = 0; i < s.Redeemed.Count; i++) _debtBook.Redeemed.Add(s.Redeemed[i]);
            _debtBook.LastDefaultPeriod = s.LastDefaultPeriod;

            // Phase 5 (P1-2): restore the issuer roster, or reseed the standard set
            // for a pre-Phase-5 save that carries none.
            _issuers.Clear();
            if (s.Issuers != null && s.Issuers.Count > 0) _issuers.AddRange(s.Issuers);
            else InitIssuersInternal();

            // Phase 5 (G-1): resume the persisted PRNG stream so post-load draws
            // continue deterministically; a save without one keeps the fresh seed.
            if (s.RngState != null && s.RngState.Length >= 4) _rng.SetState(s.RngState);

            _hazardMultiplier = s.HazardMultiplier;
            _rateVolatilityScale = s.RateVolatilityScale;
            _citizenTradingEnabled = s.CitizenTradingEnabled;
            _revenueBondsEnabled = s.RevenueBondsEnabled;

            _cashSamples = WINDOW_SIZE; // the window array is restored; treat it as populated
            _prevMoneySet = false;      // re-baseline cash tracking on the first tick after load
            _ledgerBaselineSet = false; // re-baseline the ledger diff after load
            _avgIncomePerPeriod = 0f;     // EMAs warm back up from the restored window
            _avgExpensePerPeriod = 0f;
            _totalTicks = 0;
            _lastFlowSampleTick = 0;
            _measuredTicksPerPeriod = TICKS_PER_PERIOD;
            _ticksThisPeriod = 0;
            _lastGameMonth = -1;
            _periodMetricsInitialized = false;
            _g2DiagSamples = 0; _metricRuns = 0;
        }

        private static void CopyInto(float[] dest, float[] src)
        {
            int n = src != null ? Math.Min(dest.Length, src.Length) : 0;
            for (int i = 0; i < n; i++) dest[i] = src[i];
            for (int i = n; i < dest.Length; i++) dest[i] = 0f;
        }
    }
}
