# GEX history builder (2020 to present)

Builds a daily table of SPY / QQQ / SPX / NDX gamma exposure with these columns:

`date, spot, net_gex, call_gex, put_gex, flip, call_wall, put_wall, regime, n_contracts`

The math matches the earlier yfinance script:
- gamma × OI × 100 × S² × 0.01
- dealers long calls and short puts
- 45 DTE cap
- OI ≥ 100
- IV ≥ 1%
- the flip is the zero crossing of net GEX when spot is moved ±10% at fixed (sticky-strike) IV

When a file has no IV column, IV is backed out of the bid/ask mid.

## Setup

```
pip install -r requirements.txt
python -m pytest -q tests        # synthetic checks
```

## Getting the data

Free sources only cover today's chain, so building a full history from 2020 needs a paid source for past options chains with open interest. Pick one:

| Route | Command | Notes |
|---|---|---|
| ThetaData (cheapest with historical OI) | `python build_history.py theta --root QQQ --ticker QQQ --out data/qqq_gex_daily.csv` | Needs Theta Terminal running locally. It uses the v2 endpoints `bulk_hist/option/eod` and `open_interest`. Check these against your terminal version before a long run. |
| CBOE DataShop EOD Summary, or any vendor CSV | `python build_history.py csv --path data/raw/spy/ --root SPY --ticker SPY --out data/spy_gex_daily.csv` | Column names are auto-mapped (see `ALIASES` in `gexlib/sources.py`). Each row needs date, expiration, strike, call/put, open_interest, and either IV or bid/ask. |
| SqueezeMetrics (free) | `python build_history.py squeeze --out data/squeeze_dix_gex.csv` | Gives daily SPX net GEX and DIX from 2011 on. It has **no** flip level, but it works as a regime check. |

Runs are resumable, so dates already in `--out` are skipped. For index roots (SPX/SPXW, NDX), pass `--ticker ^GSPC` or `--ticker ^NDX` for spot. If you have spot closes already, pass `--spot-csv` instead of using yfinance.

To convert to futures, multiply the flip by that day's ES/SPY (or NQ/QQQ) close ratio.

## Caveats
- The rate is a coarse table of T-bill yields (`approx_rate`), and the dividend yield is fixed at 1.3%. Gamma at ≤45 DTE barely moves with either.
- OI is end-of-day as published by the OCC, so each row reflects positioning going into the *next* session.
- Expect differences from commercial providers of tens of SPX points, for the reasons covered earlier: OI modeling, sign convention, and expiry filters. Use one source for the whole backtest.
