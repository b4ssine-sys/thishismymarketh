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
        private const int MIN_MARKET_BONDS = 6;
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
        private float _financialHealth;
        private float _citizenConfidence;
        private float _bondAppeal;
        private float _absorptionCapacity;
        private int _population;
        private float _happiness;
        private float _employmentRate;
        private float _populationGrowth;

        private float _citizenBuyVolume;
        private float _citizenSellVolume;
        private float _marketPressure;
        private float _smoothedPressure;
        private readonly float[] _pressureHistory = new float[12];
        private int _pressureHistoryIndex;

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
        public int Population { get { return _population; } }
        public string DemandLabelText { get { return CimDemandEngine.DemandLabel(_demandScore); } }
        public float CitizenBuyVolume { get { return _citizenBuyVolume; } }
        public float CitizenSellVolume { get { return _citizenSellVolume; } }
        public float MarketPressure { get { return _smoothedPressure; } }
        public string PressureLabelText { get { return CimDemandEngine.PressureLabel(_smoothedPressure); } }

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
                    return _issuedBonds.Count < MAX_ISSUED_BONDS && _rating != CreditRating.D
                        && _demandScore >= CimDemandEngine.MIN_ISSUABLE_DEMAND;
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
                    ResetStateInternal();
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
            if (scheduledDebtService <= 0f)
            {
                scheduledDebtService = avgExpense * 0.10f;
            }

            _debtBurden = avgIncome > 0f ? (scheduledDebtService / avgIncome) : 1f;
            _noi = avgIncome - avgExpense;
            _dscr = scheduledDebtService > 0f ? (_noi / scheduledDebtService) : (_noi > 0f ? 10f : 0f);

            float cashDisplay = (float)internalMoneyAmount / INTERNAL_UNIT_SCALE;
            if (cashDisplay > 500000f && _dscr < 3f) _dscr = Math.Min(_dscr + 1.0f, 10f);
            if (cashDisplay < 10000f && _dscr > 0.5f) _dscr = Math.Max(_dscr - 0.5f, 0f);

            _portfolioValue = 0f;
            for (int i = 0; i < _portfolioBonds.Count; i++)
                _portfolioValue += BondPricing.PresentValue(_portfolioBonds[i], _requiredYield);

            _rating = BondPricing.CalculateRating(_debtBurden, _dscr);
            _benchmarkRate = 0.02f + _debtBurden * 0.08f;
            if (_benchmarkRate < 0.01f) _benchmarkRate = 0.01f;
            if (_benchmarkRate > 0.15f) _benchmarkRate = 0.15f;

            float overHedgeR = CalculateOverHedgeRatioInternal();
            if (overHedgeR > 0f)
            {
                float ohPenalty = overHedgeR * 0.04f;
                if (ohPenalty > 0.10f) ohPenalty = 0.10f;
                _benchmarkRate += ohPenalty;
            }

            float baseYield = BondPricing.GetRequiredYield(_benchmarkRate, _rating);
            float defaultSpike = _defaultPenalty * (DEFAULT_YIELD_SPIKE / 25f);
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

            ReadCityDemographicsInternal();
            _financialHealth = CimDemandEngine.CalculateFinancialHealth(_rating, _dscr, _debtBurden);
            _defaultProbability = CimDemandEngine.CalculateDefaultProbability(
                _debtBurden, _dscr, _defaultPenalty, _revenueVolatility);
            _citizenConfidence = CimDemandEngine.CalculateCitizenConfidence(
                _happiness, _employmentRate, _populationGrowth);
            _bondAppeal = CimDemandEngine.CalculateBondAppeal(
                _requiredYield, _benchmarkRate, _defaultProbability);
            _demandScore = CimDemandEngine.CalculateDemandScore(
                _financialHealth, _citizenConfidence, _bondAppeal);
            _requiredYield = CimDemandEngine.AdjustYieldForDemand(_requiredYield, _demandScore);
            _requiredYield = CimDemandEngine.AdjustYieldForPressure(_requiredYield, _smoothedPressure);
            if (_requiredYield > 0.50f) _requiredYield = 0.50f;
            float avgIncomeForCap = _grossIncome / WINDOW_SIZE;
            _absorptionCapacity = CimDemandEngine.CalculateAbsorptionCapacity(
                _population, avgIncomeForCap, _demandScore);
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

        private void ReadCityDemographicsInternal()
        {
            float avgIncome = _grossIncome / WINDOW_SIZE;
            float avgExpense = _totalExpenses / WINDOW_SIZE;

            _population = Math.Max(100, (int)(avgIncome * 10f));

            float dscrHappy = _dscr / 3f;
            if (dscrHappy > 1f) dscrHappy = 1f;
            if (dscrHappy < 0f) dscrHappy = 0f;
            float noiHappy = _noi > 0f
                ? 0.6f + Math.Min(_noi / 5000f, 0.4f)
                : Math.Max(0.1f, 0.5f + _noi / 10000f);
            _happiness = dscrHappy * 0.6f + noiHappy * 0.4f;
            if (_happiness < 0f) _happiness = 0f;
            if (_happiness > 1f) _happiness = 1f;

            float totalFlow = avgIncome + avgExpense;
            _employmentRate = totalFlow > 0f ? avgIncome / totalFlow : 0.5f;
            if (_employmentRate < 0.2f) _employmentRate = 0.2f;
            if (_employmentRate > 0.98f) _employmentRate = 0.98f;

            if (_noi > 0f)
                _populationGrowth = Math.Min(_noi / 10000f, 0.05f);
            else
                _populationGrowth = Math.Max(_noi / 10000f, -0.05f);
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
            _defaultPenalty += 3;
            _totalDefaults++;
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
            if (_issuedBonds.Count == 0)
            {
                _citizenBuyVolume = 0f;
                _citizenSellVolume = 0f;
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
                        ib.SoldFraction += bought / ib.FaceValue;
                        if (ib.SoldFraction > 1f) ib.SoldFraction = 1f;
                    }
                }
            }
        }

        private void GenerateInitialBondsInternal()
        {
            _marketBonds.Clear();
            _marketBonds.Add(MakeBond("City Infrastructure Note", 10000f, 0.03f, 2));
            _marketBonds.Add(MakeBond("Transit Revenue Bond", 25000f, 0.045f, 4));
            _marketBonds.Add(MakeBond("Education Fund Bond", 50000f, 0.05f, 6));
            _marketBonds.Add(MakeBond("Water & Sewer Bond", 75000f, 0.055f, 8));
            _marketBonds.Add(MakeBond("General Obligation Bond", 100000f, 0.06f, 10));
            _marketBonds.Add(MakeBond("Capital Improvement Bond", 200000f, 0.065f, 12));
        }

        private void RegenerateBondsInternal()
        {
            while (_marketBonds.Count < MIN_MARKET_BONDS)
            {
                string issuer = MARKET_ISSUERS[_rng.Next(MARKET_ISSUERS.Length)];
                float face = MARKET_FACES[_rng.Next(MARKET_FACES.Length)];
                int term = MARKET_PERIODS[_rng.Next(MARKET_PERIODS.Length)];

                float spread = (float)(_rng.NextDouble() * 0.02 - 0.005);
                float coupon = _requiredYield + spread;
                if (coupon < 0.02f) coupon = 0.02f;
                if (coupon > 0.25f) coupon = 0.25f;

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

            while (internalAmount > 0)
            {
                int chunk = (int)Math.Min(internalAmount, (long)int.MaxValue);
                em.FetchResource(EconomyManager.Resource.LoanPayment, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                internalAmount -= chunk;
            }
            return true;
        }

        private void AddCashToCity(long internalAmount)
        {
            EconomyManager em = Singleton<EconomyManager>.instance;
            if (em == null) return;

            while (internalAmount > 0)
            {
                int chunk = (int)Math.Min(internalAmount, (long)int.MaxValue);
                em.AddResource(EconomyManager.Resource.PublicIncome, chunk,
                    ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.Level1);
                internalAmount -= chunk;
            }
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
            _activeSwaps.Clear();
            _nextSwapId = 0;
            _revenueVolatility = 0f;
            _swapPL = 0f;
            _demandScore = 0f;
            _defaultProbability = 0f;
            _financialHealth = 0f;
            _citizenConfidence = 0f;
            _bondAppeal = 0f;
            _absorptionCapacity = 0f;
            _population = 0;
            _happiness = 0.5f;
            _employmentRate = 0.7f;
            _populationGrowth = 0f;
            _citizenBuyVolume = 0f;
            _citizenSellVolume = 0f;
            _marketPressure = 0f;
            _smoothedPressure = 0f;
            Array.Clear(_pressureHistory, 0, _pressureHistory.Length);
            _pressureHistoryIndex = 0;
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

                float initialSubscription = _demandScore;
                if (initialSubscription < 0.2f) initialSubscription = 0.2f;
                if (initialSubscription > 1.0f) initialSubscription = 1.0f;

                long proceedsInternal = (long)(face * initialSubscription * INTERNAL_UNIT_SCALE);
                AddCashToCity(proceedsInternal);

                _nextBondId++;
                Bond ib = new Bond("IB" + _nextBondId.ToString(), name, face, couponRate, periods);
                ib.SoldFraction = initialSubscription;
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

        public bool TerminateSwap(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _activeSwaps.Count)
                    return false;
                _activeSwaps.RemoveAt(index);
                return true;
            }
        }

        public int TerminateAllSwaps()
        {
            lock (_lock)
            {
                int count = _activeSwaps.Count;
                _activeSwaps.Clear();
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

                if (fraction >= 1f || swap.NotionalAmount * (1f - fraction) < 1000f)
                {
                    _activeSwaps.RemoveAt(index);
                    return true;
                }

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

                    if (fraction >= 1f || swap.NotionalAmount * (1f - fraction) < 1000f)
                    {
                        _activeSwaps.RemoveAt(i);
                    }
                    else
                    {
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
    }
}
