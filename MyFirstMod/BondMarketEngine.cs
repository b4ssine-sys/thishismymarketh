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

        private const int WINDOW_SIZE = 60;
        private const int TICKS_PER_PERIOD = 15;
        private const int MIN_MARKET_BONDS = 4;
        private const int INTERNAL_UNIT_SCALE = 100;

        private readonly float[] _cashFlowHistory = new float[WINDOW_SIZE];
        private int _windowIndex;
        private long _prevMoney;
        private bool _prevMoneySet;

        private readonly List<Bond> _marketBonds = new List<Bond>();
        private readonly List<Bond> _portfolioBonds = new List<Bond>();
        private readonly object _lock = new object();

        private int _tickCounter;
        private int _nextBondId;
        private bool _initialized;

        private float _grossIncome;
        private float _totalExpenses;
        private float _debtBurden;
        private float _dscr;
        private float _noi;
        private CreditRating _rating;
        private float _benchmarkRate;
        private float _requiredYield;

        public float GrossIncome { get { return _grossIncome; } }
        public float TotalExpenses { get { return _totalExpenses; } }
        public float DebtBurden { get { return _debtBurden; } }
        public float DSCR { get { return _dscr; } }
        public float NOI { get { return _noi; } }
        public CreditRating Rating { get { return _rating; } }
        public float BenchmarkRate { get { return _benchmarkRate; } }
        public float RequiredYield { get { return _requiredYield; } }

        public override long OnUpdateMoneyAmount(long internalMoneyAmount)
        {
            Instance = this;

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

            _requiredYield = BondPricing.GetRequiredYield(_benchmarkRate, _rating);
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
                        _portfolioBonds.RemoveAt(i);
                        Debug.Log("[MyFirstMod] Bond matured: " + b.Name + " - returned face value " + b.FaceValue.ToString("N0"));
                    }
                    else
                    {
                        float couponPayment = (b.FaceValue * b.CouponRate) / BondPricing.PeriodsPerYear;
                        int couponInternal = (int)(couponPayment * INTERNAL_UNIT_SCALE);
                        if (couponInternal > 0)
                            AddCashToCity(couponInternal);
                    }
                }

                for (int i = _marketBonds.Count - 1; i >= 0; i--)
                {
                    _marketBonds[i].RemainingPeriods--;
                    if (_marketBonds[i].RemainingPeriods <= 0)
                        _marketBonds.RemoveAt(i);
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

                _portfolioBonds.RemoveAt(portfolioIndex);
                Debug.Log("[MyFirstMod] Sold bond: " + bond.Name + " for " + price.ToString("N0"));
                return true;
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

        private void GenerateInitialBonds()
        {
            lock (_lock)
            {
                _marketBonds.Clear();
                _marketBonds.Add(MakeBond("City Infrastructure Note", 10000f, 0.03f, 6));
                _marketBonds.Add(MakeBond("Transit Revenue Bond", 25000f, 0.045f, 12));
                _marketBonds.Add(MakeBond("Education Fund Bond", 50000f, 0.05f, 18));
                _marketBonds.Add(MakeBond("Water & Sewer Bond", 75000f, 0.055f, 24));
                _marketBonds.Add(MakeBond("General Obligation Bond", 100000f, 0.06f, 36));
                _marketBonds.Add(MakeBond("Capital Improvement Bond", 200000f, 0.065f, 48));
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
            int[] periods = new int[] { 6, 12, 18, 24, 36 };

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
