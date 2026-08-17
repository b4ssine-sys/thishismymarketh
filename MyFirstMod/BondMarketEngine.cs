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

        private const byte SAVE_VERSION = 4;

        private const int WINDOW_SIZE = 60;
        public const int TICKS_PER_PERIOD = 15;
        private const int MIN_MARKET_BONDS = 6;
        private const int INTERNAL_UNIT_SCALE = 100;
        private const int MAX_ISSUED_BONDS = 5;
        private const float DEFAULT_YIELD_SPIKE_PER_POINT = 0.0025f;
        private const int DEFAULT_DECAY_PER_PERIOD = 1;
        private const int DEFAULT_PENALTY_PER_EVENT = 12;

        private readonly float[] _cashFlowHistory = new float[WINDOW_SIZE];
        private int _windowIndex;
        private long _prevMoney;
        private bool _prevMoneySet;

        private readonly List<Bond> _marketBonds = new List<Bond>();
        private readonly List<Bond> _portfolioBonds = new List<Bond>();
        private readonly List<Bond> _issuedBonds = new List<Bond>();
        private readonly object _lock = new object();
        private readonly System.Random _rng = new System.Random();

        private int _tickCounter;
        private int _nextBondId;
        private bool _initialized;
        private int _defaultPenalty;
        private int _totalDefaults;
        private float _realizedPL;

        private const int MAX_ACTIVE_SWAPS = 5;
        private readonly List<InterestRateSwap> _activeSwaps = new List<InterestRateSwap>();
        private int _nextSwapId;
        private float _revenueVolatility;
        private float _swapPL;

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
        private int _demographicSampleCounter;
        private MarketState _currentMarketState;
        private MarketState _previousMarketState;

        private float _citizenBuyVolume;
        private float _citizenSellVolume;
        private float _marketPressure;
        private float _smoothedPressure;
        private readonly float[] _pressureHistory = new float[12];
        private int _pressureHistoryIndex;
        private float _citizenProceedsThisPeriod;
        private float _totalCitizenProceeds;

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
        private float _benchmarkRate;
        private float _requiredYield;
        private float _portfolioValue;

        private static readonly string[] ISSUE_NAMES = new string[]
        {
            "Emergency Note", "Municipal Note", "Revenue Bond", "Infrastructure Bond", "Capital Bond"
        };
        private static readonly float[] ISSUE_FACES = new float[] { 25000f, 75000f, 200000f, 400000f, 750000f };
        private static readonly int[] ISSUE_PERIODS = new int[] { 24, 36, 60, 84, 120 };

        private static readonly string[] MARKET_ISSUERS = new string[]
        {
            "State Transit Auth", "Regional Water District", "County Health System",
            "Port Authority", "Clean Power Grid", "District School Board"
        };
        private static readonly float[] MARKET_FACES = new float[] { 10000f, 25000f, 50000f, 75000f, 100000f, 250000f };
        private static readonly int[] MARKET_PERIODS = new int[] { 4, 6, 8, 10, 12, 16 };

        public float GrossIncome { get { return _grossIncome; } }
        public float TotalExpenses { get { return _totalExpenses; } }
        public float DebtBurden { get { return _debtBurden; } }
        public float DSCR { get { return _dscr; } }
        public float NOI { get { return _noi; } }
        public CreditRating Rating { get { return _rating; } }
        public float BenchmarkRate { get { return _benchmarkRate; } }
        public float RequiredYield { get { return _requiredYield; } }
        public float PortfolioValue { get { return _portfolioValue; } }
        public int DefaultPenalty { get { return _defaultPenalty; } }
        public int TotalDefaults { get { return _totalDefaults; } }
        public float RealizedPL { get { return _realizedPL; } }
        public int TicksInCurrentPeriod { get { return _tickCounter; } }

        public int IssuedCount { get { lock (_lock) { return _issuedBonds.Count; } } }
        public int MaxIssuedBonds { get { return MAX_ISSUED_BONDS; } }
        public int IssueTemplateCount { get { return ISSUE_NAMES.Length; } }

        public int PortfolioCount { get { lock (_lock) { return _portfolioBonds.Count; } } }
        public int MarketCount { get { lock (_lock) { return _marketBonds.Count; } } }

        public float RevenueVolatility { get { return _revenueVolatility; } }
        public float SwapPL { get { return _swapPL; } }
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
        public float TotalCitizenProceeds { get { return _totalCitizenProceeds; } }
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
                        total += ib.SubscribedFace + remainingCoupons;
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

        public string GetTemplateName(int index) { return ISSUE_NAMES[index]; }
        public float GetTemplateFace(int index) { return ISSUE_FACES[index]; }
        public int GetTemplatePeriods(int index) { return ISSUE_PERIODS[index]; }

        public override long OnUpdateMoneyAmount(long internalMoneyAmount)
        {
            Instance = this;

            lock (_lock)
            {
                if (NeedsReset)
                {
                    NeedsReset = false;
                    PendingSaveData = null;
                    ResetStateInternal();
                }
                else if (PendingSaveData != null)
                {
                    byte[] data = PendingSaveData;
                    PendingSaveData = null;
                    RestoreState(data);
                }

                UpdateCashFlowHistory(internalMoneyAmount);
                RecalculateMetricsInternal(internalMoneyAmount);

                _tickCounter++;
                if (_tickCounter >= TICKS_PER_PERIOD)
                {
                    _tickCounter = 0;
                    AgeBondsInternal();
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
            }

            return internalMoneyAmount;
        }

        private void UpdateCashFlowHistory(long internalMoneyAmount)
        {
            if (_prevMoneySet)
            {
                float delta = (float)(internalMoneyAmount - _prevMoney);
                _cashFlowHistory[_windowIndex] = delta;
                _windowIndex = (_windowIndex + 1) % WINDOW_SIZE;
            }
            _prevMoney = internalMoneyAmount;
            _prevMoneySet = true;
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

            _grossIncome = totalPositive / INTERNAL_UNIT_SCALE;
            _totalExpenses = totalNegative / INTERNAL_UNIT_SCALE;

            float avgIncome = _grossIncome / WINDOW_SIZE;
            float avgExpense = _totalExpenses / WINDOW_SIZE;

            float scheduledDebtService = CalculateActiveDebtService();

            _noi = avgIncome - avgExpense;

            if (scheduledDebtService > 0f)
            {
                _debtBurden = avgIncome > 0f ? (scheduledDebtService / avgIncome) : 1f;
                _dscr = _noi / scheduledDebtService;
            }
            else
            {
                _debtBurden = 0f;
                _dscr = avgIncome > 0f ? 10f : 0f;
            }

            float cashDisplay = (float)internalMoneyAmount / INTERNAL_UNIT_SCALE;
            if (cashDisplay > 500000f && _dscr < 3f) _dscr = Math.Min(_dscr + 1.0f, 10f);
            if (cashDisplay < 10000f && _dscr > 0.5f) _dscr = Math.Max(_dscr - 0.5f, 0f);

            _portfolioValue = 0f;
            for (int i = 0; i < _portfolioBonds.Count; i++)
                _portfolioValue += BondPricing.PresentValue(_portfolioBonds[i], _requiredYield);

            _rating = BondPricing.CalculateRating(_debtBurden, _dscr);

            float fedFundsProxy = 0.04f;
            float termPremium = 0.005f + _revenueVolatility * 0.01f;
            if (termPremium > 0.02f) termPremium = 0.02f;
            float fiscalAdj = _debtBurden * 0.02f;
            _benchmarkRate = fedFundsProxy + termPremium + fiscalAdj;
            if (_benchmarkRate < 0.025f) _benchmarkRate = 0.025f;
            if (_benchmarkRate > 0.08f) _benchmarkRate = 0.08f;

            float overHedgeR = CalculateOverHedgeRatioInternal();
            if (overHedgeR > 0f)
            {
                float ohPenalty = overHedgeR * 0.015f;
                if (ohPenalty > 0.03f) ohPenalty = 0.03f;
                _benchmarkRate += ohPenalty;
            }

            float baseYield = BondPricing.GetRequiredYield(_benchmarkRate, _rating);
            float defaultSpike = _defaultPenalty * DEFAULT_YIELD_SPIKE_PER_POINT;
            _requiredYield = baseYield + defaultSpike;

            float totalWealth = cashDisplay + _portfolioValue;
            float wealthBase = avgIncome * WINDOW_SIZE;
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
                float maxDelta = 0.02f;
                float delta = _requiredYield - _prevRequiredYield;
                if (delta > maxDelta) _requiredYield = _prevRequiredYield + maxDelta;
                else if (delta < -maxDelta) _requiredYield = _prevRequiredYield - maxDelta;
            }
            _prevRequiredYield = _requiredYield;
            _absorptionCapacity = CimDemandEngine.CalculateAbsorptionCapacity(
                _population, _cashReserves, _demandScore);
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

        private void ReadCityDemographicsInternal(float cashDisplay)
        {
            _cashReserves = cashDisplay;

            bool gotGameData = false;

            try
            {
                DistrictManager dm = Singleton<DistrictManager>.instance;
                if (dm != null)
                {
                    District city = dm.m_districts.m_buffer[0];

                    uint realPop = city.m_populationData.m_finalCount;
                    if (realPop > 0)
                    {
                        _population = (int)realPop;
                        gotGameData = true;
                    }

                    _happiness = city.m_finalHappiness / 100f;
                }
            }
            catch
            {
                float avgIncome = _grossIncome / WINDOW_SIZE;
                float avgExpense = _totalExpenses / WINDOW_SIZE;

                if (_population < 100) _population = Math.Max(100, (int)(avgIncome * 10f));

                float dscrH = _dscr / 3f;
                if (dscrH > 1f) dscrH = 1f;
                if (dscrH < 0f) dscrH = 0f;
                _happiness = dscrH;
                _landValue = dscrH * 0.5f + 0.25f;
                _crimeRate = Math.Max(0f, 0.5f - dscrH * 0.4f);

                float totalFlow = avgIncome + avgExpense;
                _employmentRate = totalFlow > 0f ? avgIncome / totalFlow : 0.5f;
            }

            try
            {
                CitizenManager cm = Singleton<CitizenManager>.instance;
                if (cm != null)
                {
                    if (!gotGameData && cm.m_citizenCount > 0)
                        _population = cm.m_citizenCount;

                    if (_demographicSampleCounter == 0)
                    {
                        float healthSum = 0f;
                        float eduSum = 0f;
                        float wellbeingSum = 0f;
                        int sampled = 0;
                        uint bufSize = cm.m_citizens.m_size;
                        int step = Math.Max(1, (int)(bufSize / 200));
                        int employed = 0;

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
                }
            }
            catch
            {
                float avgIncome = _grossIncome / WINDOW_SIZE;
                float avgExpense = _totalExpenses / WINDOW_SIZE;
                float dscrH = Math.Min(Math.Max(_dscr / 3f, 0f), 1f);

                if (_health <= 0f) _health = dscrH * 0.8f + 0.2f;
                if (_education <= 0f) _education = 0.5f;
                if (_landValue <= 0f) _landValue = dscrH * 0.5f + 0.25f;
                _crimeRate = Math.Max(0f, 0.5f - dscrH * 0.4f);
                if (_employmentRate <= 0.2f)
                {
                    float totalFlow = avgIncome + avgExpense;
                    _employmentRate = totalFlow > 0f ? avgIncome / totalFlow : 0.5f;
                }
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

            _demographicSampleCounter++;
            if (_demographicSampleCounter >= TICKS_PER_PERIOD)
            {
                _demographicSampleCounter = 0;
                if (_prevPopulation > 0)
                {
                    _populationGrowth = (float)(_population - _prevPopulation) / (float)_prevPopulation;
                    if (_populationGrowth < -0.05f) _populationGrowth = -0.05f;
                    if (_populationGrowth > 0.05f) _populationGrowth = 0.05f;
                }
                _prevPopulation = _population;
            }
        }

        private float CalculateActiveDebtService()
        {
            float total = 0f;
            for (int i = 0; i < _issuedBonds.Count; i++)
            {
                Bond ib = _issuedBonds[i];
                total += (ib.SubscribedFace * ib.CouponRate) / BondPricing.PeriodsPerYear;
            }
            return total;
        }

        private void AgeBondsInternal()
        {
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
                    float couponPayment = (b.FaceValue * b.CouponRate) / BondPricing.PeriodsPerYear;
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
            SimulateCitizenTradingInternal();

            if (_defaultPenalty > 0)
            {
                _defaultPenalty = Math.Max(0, _defaultPenalty - DEFAULT_DECAY_PER_PERIOD);
            }

            _periodsSinceReport++;
            if (_periodsSinceReport >= PERIODS_PER_QUARTER)
            {
                _periodsSinceReport = 0;
                GenerateQuarterlyReportInternal();
            }
        }

        private void ServiceIssuedBondsInternal()
        {
            for (int i = _issuedBonds.Count - 1; i >= 0; i--)
            {
                Bond ib = _issuedBonds[i];
                ib.RemainingPeriods--;

                if (ib.RemainingPeriods <= 0)
                {
                    long faceInternal = (long)(ib.SubscribedFace * INTERNAL_UNIT_SCALE);
                    if (!TrySpendCash(faceInternal))
                    {
                        TriggerDefaultInternal(ib, "maturity repayment");
                        _issuedBonds.RemoveAt(i);
                        continue;
                    }
                    ib.CouponsReceived += ib.SubscribedFace;
                    _issuedBonds.RemoveAt(i);
                }
                else
                {
                    float couponPayment = (ib.SubscribedFace * ib.CouponRate) / BondPricing.PeriodsPerYear;
                    long couponInternal = (long)(couponPayment * INTERNAL_UNIT_SCALE);
                    if (couponInternal > 0)
                    {
                        if (!TrySpendCash(couponInternal))
                        {
                            TriggerDefaultInternal(ib, "coupon payment");
                            _issuedBonds.RemoveAt(i);
                            continue;
                        }
                        ib.CouponsReceived += couponPayment;
                    }
                }
            }
        }

        private void TriggerDefaultInternal(Bond bond, string reason)
        {
            _defaultPenalty += DEFAULT_PENALTY_PER_EVENT;
            _totalDefaults++;
            _quarterDefaults++;
        }

        private void SettleSwapsInternal()
        {
            float floatingRate = _benchmarkRate;

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
                    {
                        _activeSwaps.RemoveAt(i);
                        continue;
                    }
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

            float[] beforeFractions = new float[_issuedBonds.Count];
            for (int i = 0; i < _issuedBonds.Count; i++)
                beforeFractions[i] = _issuedBonds[i].SoldFraction;

            if (_citizenBuyVolume > 0f)
            {
                float totalUnsold = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    totalUnsold += _issuedBonds[i].FaceValue * (1f - _issuedBonds[i].SoldFraction);

                if (totalUnsold > 0f)
                {
                    float buyable = _citizenBuyVolume;
                    if (buyable > totalUnsold) buyable = totalUnsold;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                    {
                        Bond ib = _issuedBonds[i];
                        float unsold = ib.FaceValue * (1f - ib.SoldFraction);
                        if (unsold <= 0f) continue;

                        float share = unsold / totalUnsold;
                        float bought = buyable * share;
                        float fractionBought = bought / ib.FaceValue;
                        ib.SoldFraction += fractionBought;
                        if (ib.SoldFraction > 1f) ib.SoldFraction = 1f;

                        long proceeds = (long)(bought * INTERNAL_UNIT_SCALE);
                        if (proceeds > 0)
                        {
                            AddCashToCity(proceeds);
                            _citizenProceedsThisPeriod += bought;
                            _totalCitizenProceeds += bought;
                        }
                    }
                }
            }

            if (_citizenSellVolume > 0f)
            {
                float totalSold = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    totalSold += _issuedBonds[i].FaceValue * _issuedBonds[i].SoldFraction;

                if (totalSold > 0f)
                {
                    float sellable = _citizenSellVolume;
                    if (sellable > totalSold) sellable = totalSold;
                    for (int i = 0; i < _issuedBonds.Count; i++)
                    {
                        Bond ib = _issuedBonds[i];
                        float soldValue = ib.FaceValue * ib.SoldFraction;
                        if (soldValue <= 0f) continue;

                        float share = soldValue / totalSold;
                        float sold = sellable * share;
                        float prevFraction = ib.SoldFraction;
                        float fractionSold = sold / ib.FaceValue;
                        ib.SoldFraction -= fractionSold;
                        if (ib.SoldFraction < 0.05f) ib.SoldFraction = 0.05f;

                        float actualRedeemed = (prevFraction - ib.SoldFraction) * ib.FaceValue;
                        if (actualRedeemed > 0f)
                        {
                            long cost = (long)(actualRedeemed * INTERNAL_UNIT_SCALE);
                            TrySpendCash(cost);
                        }
                    }
                }
            }

            string detail = "";
            float periodProceeds = _citizenProceedsThisPeriod;
            for (int i = 0; i < _issuedBonds.Count; i++)
            {
                Bond ib = _issuedBonds[i];
                float before = beforeFractions[i];
                float after = ib.SoldFraction;
                float delta = after - before;
                if (delta > 0.001f || delta < -0.001f)
                {
                    if (detail.Length > 0) detail += "  ";
                    string arrow = delta > 0 ? ">" : "<";
                    detail += string.Format("{0}: {1:F0}%{2}{3:F0}%",
                        ib.Id, before * 100f, arrow, after * 100f);
                }
            }
            if (detail.Length == 0)
            {
                if (_citizenBuyVolume > _citizenSellVolume)
                    detail = "Fully subscribed";
                else
                    detail = "Sell pressure only";
            }
            if (periodProceeds > 0f)
                detail += string.Format("  +{0:N0} proceeds", periodProceeds);

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
                debtOwed += ib.SubscribedFace + rc;
                totalSub += ib.SoldFraction;
                couponsPaid += ib.CouponsReceived;
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

            rp.RealizedPL = _realizedPL;
            rp.SwapPL = _swapPL;
            rp.RevenueVolatility = _revenueVolatility;
            rp.Happiness = _happiness;
            rp.EmploymentRate = _employmentRate;
            rp.PopulationGrowth = _populationGrowth;
            rp.CitizenConfidence = _citizenConfidence;
            rp.BondAppeal = _bondAppeal;
            rp.FinancialHealth = _financialHealth;
            rp.CitizenProceeds = _totalCitizenProceeds;
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
            _marketBonds.Clear();
            _marketBonds.Add(MakeBond("City Infrastructure Note", 10000f, 0.042f, 2));
            _marketBonds.Add(MakeBond("Transit Revenue Bond", 25000f, 0.047f, 4));
            _marketBonds.Add(MakeBond("Education Fund Bond", 50000f, 0.050f, 6));
            _marketBonds.Add(MakeBond("Water & Sewer Bond", 75000f, 0.053f, 8));
            _marketBonds.Add(MakeBond("General Obligation Bond", 100000f, 0.055f, 10));
            _marketBonds.Add(MakeBond("Capital Improvement Bond", 200000f, 0.058f, 12));
        }

        private void RegenerateBondsInternal()
        {
            while (_marketBonds.Count < MIN_MARKET_BONDS)
            {
                string issuer = MARKET_ISSUERS[_rng.Next(MARKET_ISSUERS.Length)];
                float face = MARKET_FACES[_rng.Next(MARKET_FACES.Length)];
                int term = MARKET_PERIODS[_rng.Next(MARKET_PERIODS.Length)];

                float spread = (float)(_rng.NextDouble() * 0.012 - 0.003);
                float coupon = _requiredYield + spread;
                if (coupon < 0.025f) coupon = 0.025f;
                if (coupon > 0.10f) coupon = 0.10f;

                _marketBonds.Add(MakeBond(issuer, face, coupon, term));
            }
        }

        private Bond MakeBond(string name, float face, float coupon, int periods)
        {
            _nextBondId++;
            return new Bond("B" + _nextBondId.ToString(), name, face, coupon, periods);
        }

        private bool TrySpendCash(long internalAmount)
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null || em.LastCashAmount < internalAmount) return false;

            long original = internalAmount;
            while (internalAmount > 0)
            {
                int chunk = (int)Math.Min(internalAmount, (long)int.MaxValue);
                em.FetchResource(EconomyManager.Resource.LoanPayment, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                internalAmount -= chunk;
            }
            _prevMoney -= original;
            return true;
        }

        private void AddCashToCity(long internalAmount)
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return;

            long original = internalAmount;
            while (internalAmount > 0)
            {
                int chunk = (int)Math.Min(internalAmount, (long)int.MaxValue);
                em.AddResource(EconomyManager.Resource.PublicIncome, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                internalAmount -= chunk;
            }
            _prevMoney += original;
        }

        private void ResetStateInternal()
        {
            _marketBonds.Clear();
            _portfolioBonds.Clear();
            _issuedBonds.Clear();
            Array.Clear(_cashFlowHistory, 0, _cashFlowHistory.Length);

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
            _portfolioValue = 0f;
            _prevRequiredYield = 0f;
            _activeSwaps.Clear();
            _nextSwapId = 0;
            _revenueVolatility = 0f;
            _swapPL = 0f;
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
            _demographicSampleCounter = 0;
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

            if (PendingSaveData != null)
            {
                try
                {
                    RestoreState(PendingSaveData);
                    _initialized = true;
                    Debug.Log("[MyFirstMod] Bond market state restored from save.");
                }
                catch (Exception ex)
                {
                    Debug.Log("[MyFirstMod] Failed to restore save data: " + ex.Message);
                }
                PendingSaveData = null;
            }
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

        public bool BuyBond(int marketIndex)
        {
            lock (_lock)
            {
                if (marketIndex < 0 || marketIndex >= _marketBonds.Count)
                    return false;

                Bond bond = _marketBonds[marketIndex];
                float price = BondPricing.PresentValue(bond, _requiredYield);
                long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                if (!TrySpendCash(priceInternal))
                    return false;

                bond.PurchasePrice = price;
                _portfolioBonds.Add(bond);
                _marketBonds.RemoveAt(marketIndex);
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
                    float price = BondPricing.PresentValue(bond, _requiredYield);
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
                if (_demandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND)
                    return false;

                string name = ISSUE_NAMES[optionIndex];
                float face = ISSUE_FACES[optionIndex];
                int periods = ISSUE_PERIODS[optionIndex];

                float currentFace = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    currentFace += _issuedBonds[i].FaceValue;
                if (currentFace + face > _absorptionCapacity)
                    return false;

                float couponRate = _requiredYield;

                _nextBondId++;
                Bond ib = new Bond("IB" + _nextBondId.ToString(), name, face, couponRate, periods);
                ib.SoldFraction = 0f;
                _issuedBonds.Add(ib);
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
                if (_demandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND)
                    return false;

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
                float couponRate = _requiredYield;

                _nextBondId++;
                string name = string.Format("{0:F0}% Bank Bond", percent * 100f);
                Bond ib = new Bond("IB" + _nextBondId.ToString(), name, face, couponRate, periods);
                ib.SoldFraction = 0f;
                _issuedBonds.Add(ib);
                return true;
            }
        }

        public int PayDebtPercent(float percent)
        {
            lock (_lock)
            {
                if (_issuedBonds.Count == 0)
                    return 0;

                float totalFace = 0f;
                for (int i = 0; i < _issuedBonds.Count; i++)
                    totalFace += _issuedBonds[i].SubscribedFace;

                float budget = totalFace * percent;
                int retired = 0;

                for (int i = _issuedBonds.Count - 1; i >= 0; i--)
                {
                    Bond ib = _issuedBonds[i];
                    if (ib.SubscribedFace > budget)
                        continue;

                    long faceInternal = (long)(ib.SubscribedFace * INTERNAL_UNIT_SCALE);
                    if (!TrySpendCash(faceInternal))
                        continue;

                    budget -= ib.SubscribedFace;
                    _issuedBonds.RemoveAt(i);
                    retired++;
                }

                if (retired == 0 && budget > 0f && _issuedBonds.Count > 0)
                {
                    int smallest = 0;
                    for (int i = 1; i < _issuedBonds.Count; i++)
                    {
                        if (_issuedBonds[i].SubscribedFace < _issuedBonds[smallest].SubscribedFace)
                            smallest = i;
                    }

                    Bond sb = _issuedBonds[smallest];
                    float paydown = budget;
                    if (paydown > sb.SubscribedFace)
                        paydown = sb.SubscribedFace;

                    long payInternal = (long)(paydown * INTERNAL_UNIT_SCALE);
                    if (payInternal > 0 && TrySpendCash(payInternal))
                    {
                        float newSubscribed = sb.SubscribedFace - paydown;
                        if (newSubscribed < 1f)
                        {
                            _issuedBonds.RemoveAt(smallest);
                            retired++;
                        }
                        else
                        {
                            sb.SoldFraction = newSubscribed / sb.FaceValue;
                            retired = -1;
                        }
                    }
                }

                return retired;
            }
        }

        public bool Buy1BBond()
        {
            lock (_lock)
            {
                float face = 1000000000f;
                float coupon = _requiredYield;
                int periods = 60;

                Bond b = MakeBond("Institutional Sovereign Note", face, coupon, periods);
                float price = BondPricing.PresentValue(b, _requiredYield);
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
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return 0;
                long remaining = em.LastCashAmount;

                float coupon = _requiredYield;
                int periods = 60;
                int bought = 0;

                for (int i = 0; i < 10; i++)
                {
                    Bond b = MakeBond("Corporate Tranche Note", 1000000f, coupon, periods);
                    float price = BondPricing.PresentValue(b, _requiredYield);
                    long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                    if (remaining < priceInternal)
                        break;

                    remaining -= priceInternal;
                    if (!TrySpendCash(priceInternal))
                        break;
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
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return 0;
                long remaining = em.LastCashAmount;

                float coupon = _requiredYield;
                int periods = 60;
                int bought = 0;

                for (int i = 0; i < 10; i++)
                {
                    Bond b = MakeBond("10M Treasury Bond", 10000000f, coupon, periods);
                    float price = BondPricing.PresentValue(b, _requiredYield);
                    long priceInternal = (long)(price * INTERNAL_UNIT_SCALE);

                    if (remaining < priceInternal)
                        break;

                    remaining -= priceInternal;
                    if (!TrySpendCash(priceInternal))
                        break;
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
            float floatingRate = _benchmarkRate;
            float remainingYears = (float)swap.RemainingPeriods / BondPricing.PeriodsPerYear;
            if (swap.PayFixed)
                return (floatingRate - swap.FixedRate) * swap.NotionalAmount * remainingYears;
            else
                return (swap.FixedRate - floatingRate) * swap.NotionalAmount * remainingYears;
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

        public bool TerminateSwap(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _activeSwaps.Count)
                    return false;

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

        public bool SellSwapTranche(int index, float fraction)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _activeSwaps.Count)
                    return false;
                if (fraction <= 0f || fraction > 1f)
                    return false;

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

                float remainFraction = 1f - fraction;
                swap.NotionalAmount *= remainFraction;
                swap.CumulativePL *= remainFraction;
                return true;
            }
        }

        public int SellAllSwapsTranche(float fraction)
        {
            lock (_lock)
            {
                if (fraction <= 0f || fraction > 1f)
                    return 0;

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
                        float remainFraction = 1f - fraction;
                        swap.NotionalAmount *= remainFraction;
                        swap.CumulativePL *= remainFraction;
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

                _nextSwapId++;
                InterestRateSwap swap = new InterestRateSwap(
                    "SW" + _nextSwapId.ToString(), unhedged, _requiredYield, avgPeriods, true);
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

        public void GetActiveSwapsSnapshot(List<InterestRateSwap> outSwaps)
        {
            outSwaps.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _activeSwaps.Count; i++)
                    outSwaps.Add(_activeSwaps[i]);
            }
        }

        public void GetIssuedBondsSnapshot(List<Bond> outBonds)
        {
            outBonds.Clear();
            lock (_lock)
            {
                for (int i = 0; i < _issuedBonds.Count; i++)
                    outBonds.Add(_issuedBonds[i]);
            }
        }

        public bool RepaySingleBond(int issuedIndex)
        {
            lock (_lock)
            {
                if (issuedIndex < 0 || issuedIndex >= _issuedBonds.Count)
                    return false;

                Bond ib = _issuedBonds[issuedIndex];
                long faceInternal = (long)(ib.SubscribedFace * INTERNAL_UNIT_SCALE);
                if (!TrySpendCash(faceInternal))
                    return false;

                _issuedBonds.RemoveAt(issuedIndex);
                return true;
            }
        }

        public byte[] SerializeState()
        {
            lock (_lock)
            {
                try
                {
                    MemoryStream ms = new MemoryStream();
                    BinaryWriter w = new BinaryWriter(ms);

                    w.Write(SAVE_VERSION);

                    w.Write(_nextBondId);
                    w.Write(_nextSwapId);
                    w.Write(_tickCounter);
                    w.Write(_defaultPenalty);
                    w.Write(_totalDefaults);
                    w.Write(_realizedPL);
                    w.Write(_swapPL);
                    w.Write(_windowIndex);
                    w.Write(_initialized);
                    w.Write(_transactionSeq);
                    w.Write(_pressureHistoryIndex);

                    for (int i = 0; i < WINDOW_SIZE; i++)
                        w.Write(_cashFlowHistory[i]);

                    for (int i = 0; i < _pressureHistory.Length; i++)
                        w.Write(_pressureHistory[i]);

                    WriteBondList(w, _portfolioBonds);
                    WriteBondList(w, _issuedBonds);
                    WriteBondList(w, _marketBonds);

                    w.Write(_activeSwaps.Count);
                    for (int i = 0; i < _activeSwaps.Count; i++)
                    {
                        InterestRateSwap s = _activeSwaps[i];
                        w.Write(s.Id);
                        w.Write(s.NotionalAmount);
                        w.Write(s.FixedRate);
                        w.Write(s.TotalPeriods);
                        w.Write(s.RemainingPeriods);
                        w.Write(s.PayFixed);
                        w.Write(s.CumulativePL);
                        w.Write(s.LastSettlement);
                    }

                    w.Write(_transactionLog.Count);
                    for (int i = 0; i < _transactionLog.Count; i++)
                    {
                        CimTransaction tx = _transactionLog[i];
                        w.Write(tx.Sequence);
                        w.Write(tx.BuyVolume);
                        w.Write(tx.SellVolume);
                        w.Write(tx.Pressure);
                        w.Write(tx.Detail != null ? tx.Detail : "");
                    }

                    w.Write(_periodsSinceReport);
                    w.Write(_quarterNumber);
                    w.Write(_quarterDefaults);
                    w.Write(_reportHistory.Count);
                    for (int i = 0; i < _reportHistory.Count; i++)
                    {
                        QuarterlyReport rp = _reportHistory[i];
                        w.Write(rp.Quarter);
                        w.Write((int)rp.Rating);
                        w.Write(rp.CreditStatus != null ? rp.CreditStatus : "");
                        w.Write(rp.DSCR);
                        w.Write(rp.DebtBurden);
                        w.Write(rp.GrossIncome);
                        w.Write(rp.TotalExpenses);
                        w.Write(rp.NOI);
                        w.Write(rp.DefaultProbability);
                        w.Write(rp.IssuedBonds);
                        w.Write(rp.MaxBonds);
                        w.Write(rp.DebtFace);
                        w.Write(rp.DebtOwed);
                        w.Write(rp.AvgSubscription);
                        w.Write(rp.CouponsPaid);
                        w.Write(rp.QuarterDefaults);
                        w.Write(rp.TotalDefaults);
                        w.Write(rp.BenchmarkRate);
                        w.Write(rp.RequiredYield);
                        w.Write(rp.DemandScore);
                        w.Write(rp.SmoothedPressure);
                        w.Write(rp.AbsorptionCapacity);
                        w.Write(rp.Population);
                        w.Write(rp.PortfolioBonds);
                        w.Write(rp.SwapCount);
                        w.Write(rp.HedgedNotional);
                        w.Write(rp.RealizedPL);
                        w.Write(rp.SwapPL);
                        w.Write(rp.RevenueVolatility);
                        w.Write(rp.Outlook != null ? rp.Outlook : "");
                        w.Write(rp.Happiness);
                        w.Write(rp.EmploymentRate);
                        w.Write(rp.PopulationGrowth);
                        w.Write(rp.CitizenConfidence);
                        w.Write(rp.BondAppeal);
                        w.Write(rp.FinancialHealth);
                        w.Write(rp.CitizenProceeds);
                    }

                    w.Write(_totalCitizenProceeds);

                    w.Flush();
                    return ms.ToArray();
                }
                catch (Exception ex)
                {
                    Debug.Log("[MyFirstMod] SerializeState failed: " + ex.Message);
                    return null;
                }
            }
        }

        private void RestoreState(byte[] data)
        {
            try
            {
                MemoryStream ms = new MemoryStream(data);
                BinaryReader r = new BinaryReader(ms);

                byte version = r.ReadByte();
                if (version < 1 || version > SAVE_VERSION)
                {
                    Debug.Log("[MyFirstMod] RestoreState: Unknown version " + version + ", skipping.");
                    return;
                }

                _nextBondId = r.ReadInt32();
                _nextSwapId = r.ReadInt32();
                _tickCounter = r.ReadInt32();
                _defaultPenalty = r.ReadInt32();
                _totalDefaults = r.ReadInt32();
                _realizedPL = r.ReadSingle();
                _swapPL = r.ReadSingle();
                _windowIndex = r.ReadInt32();
                _initialized = r.ReadBoolean();
                _transactionSeq = r.ReadInt32();
                _pressureHistoryIndex = r.ReadInt32();

                for (int i = 0; i < WINDOW_SIZE; i++)
                    _cashFlowHistory[i] = r.ReadSingle();

                for (int i = 0; i < _pressureHistory.Length; i++)
                    _pressureHistory[i] = r.ReadSingle();

                ReadBondList(r, _portfolioBonds);
                ReadBondList(r, _issuedBonds);
                ReadBondList(r, _marketBonds);

                _activeSwaps.Clear();
                int swapCount = r.ReadInt32();
                for (int i = 0; i < swapCount; i++)
                {
                    string id = r.ReadString();
                    float notional = r.ReadSingle();
                    float fixedRate = r.ReadSingle();
                    int totalP = r.ReadInt32();
                    int remainP = r.ReadInt32();
                    bool payFixed = r.ReadBoolean();
                    float cumPL = r.ReadSingle();
                    float lastS = r.ReadSingle();

                    InterestRateSwap s = new InterestRateSwap(id, notional, fixedRate, totalP, payFixed);
                    s.RemainingPeriods = remainP;
                    s.CumulativePL = cumPL;
                    s.LastSettlement = lastS;
                    _activeSwaps.Add(s);
                }

                _transactionLog.Clear();
                int txCount = r.ReadInt32();
                for (int i = 0; i < txCount; i++)
                {
                    CimTransaction tx = new CimTransaction();
                    tx.Sequence = r.ReadInt32();
                    tx.BuyVolume = r.ReadSingle();
                    tx.SellVolume = r.ReadSingle();
                    tx.Pressure = r.ReadSingle();
                    tx.Detail = r.ReadString();
                    _transactionLog.Add(tx);
                }

                _prevMoneySet = false;

                if (version >= 2)
                {
                    _periodsSinceReport = r.ReadInt32();
                    _quarterNumber = r.ReadInt32();
                    _quarterDefaults = r.ReadInt32();
                    _reportHistory.Clear();
                    int reportCount = r.ReadInt32();
                    for (int i = 0; i < reportCount; i++)
                    {
                        QuarterlyReport rp = new QuarterlyReport();
                        rp.Quarter = r.ReadInt32();
                        rp.Rating = (CreditRating)r.ReadInt32();
                        rp.CreditStatus = r.ReadString();
                        rp.DSCR = r.ReadSingle();
                        rp.DebtBurden = r.ReadSingle();
                        rp.GrossIncome = r.ReadSingle();
                        rp.TotalExpenses = r.ReadSingle();
                        rp.NOI = r.ReadSingle();
                        rp.DefaultProbability = r.ReadSingle();
                        rp.IssuedBonds = r.ReadInt32();
                        rp.MaxBonds = r.ReadInt32();
                        rp.DebtFace = r.ReadSingle();
                        rp.DebtOwed = r.ReadSingle();
                        rp.AvgSubscription = r.ReadSingle();
                        rp.CouponsPaid = r.ReadSingle();
                        rp.QuarterDefaults = r.ReadInt32();
                        rp.TotalDefaults = r.ReadInt32();
                        rp.BenchmarkRate = r.ReadSingle();
                        rp.RequiredYield = r.ReadSingle();
                        rp.DemandScore = r.ReadSingle();
                        rp.SmoothedPressure = r.ReadSingle();
                        rp.AbsorptionCapacity = r.ReadSingle();
                        rp.Population = r.ReadInt32();
                        rp.PortfolioBonds = r.ReadInt32();
                        rp.SwapCount = r.ReadInt32();
                        rp.HedgedNotional = r.ReadSingle();
                        rp.RealizedPL = r.ReadSingle();
                        rp.SwapPL = r.ReadSingle();
                        rp.RevenueVolatility = r.ReadSingle();
                        rp.Outlook = r.ReadString();
                        if (version >= 4)
                        {
                            rp.Happiness = r.ReadSingle();
                            rp.EmploymentRate = r.ReadSingle();
                            rp.PopulationGrowth = r.ReadSingle();
                            rp.CitizenConfidence = r.ReadSingle();
                            rp.BondAppeal = r.ReadSingle();
                            rp.FinancialHealth = r.ReadSingle();
                            rp.CitizenProceeds = r.ReadSingle();
                        }
                        _reportHistory.Add(rp);
                    }
                }

                if (version >= 3)
                {
                    _totalCitizenProceeds = r.ReadSingle();
                }

                Debug.Log("[MyFirstMod] RestoreState: OK. Bonds P/I/M=" +
                    _portfolioBonds.Count + "/" + _issuedBonds.Count + "/" + _marketBonds.Count +
                    " Swaps=" + _activeSwaps.Count + " Reports=" + _reportHistory.Count);
            }
            catch (Exception ex)
            {
                Debug.Log("[MyFirstMod] RestoreState failed: " + ex.Message + " - resetting to fresh state.");
                ResetStateInternal();
            }
        }

        private static void WriteBondList(BinaryWriter w, List<Bond> bonds)
        {
            w.Write(bonds.Count);
            for (int i = 0; i < bonds.Count; i++)
            {
                Bond b = bonds[i];
                w.Write(b.Id);
                w.Write(b.Name);
                w.Write(b.FaceValue);
                w.Write(b.CouponRate);
                w.Write(b.TotalPeriods);
                w.Write(b.RemainingPeriods);
                w.Write(b.PurchasePrice);
                w.Write(b.CouponsReceived);
                w.Write(b.SoldFraction);
            }
        }

        private static void ReadBondList(BinaryReader r, List<Bond> bonds)
        {
            bonds.Clear();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string id = r.ReadString();
                string name = r.ReadString();
                float face = r.ReadSingle();
                float coupon = r.ReadSingle();
                int totalP = r.ReadInt32();
                int remainP = r.ReadInt32();
                float purchase = r.ReadSingle();
                float couponsRcvd = r.ReadSingle();
                float soldFrac = r.ReadSingle();

                Bond b = new Bond(id, name, face, coupon, totalP);
                b.RemainingPeriods = remainP;
                b.PurchasePrice = purchase;
                b.CouponsReceived = couponsRcvd;
                b.SoldFraction = soldFrac;
                bonds.Add(b);
            }
        }
    }
}
