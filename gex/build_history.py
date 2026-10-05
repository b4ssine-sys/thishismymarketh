"""Build a daily GEX history (net GEX, gamma flip, call/put walls, regime).

Examples
--------
# From vendor CSVs (CBOE DataShop EOD Summary, ThetaData exports, etc.)
python build_history.py csv --path data/raw/spy/ --root SPY --ticker SPY \
    --start 2020-01-01 --out data/spy_gex_daily.csv

# Straight from a running Theta Terminal
python build_history.py theta --root QQQ --ticker QQQ --start 2020-01-01 \
    --out data/qqq_gex_daily.csv

# Free SqueezeMetrics daily SPX GEX number (no flip level)
python build_history.py squeeze --start 2020-01-01 --out data/squeeze_dix_gex.csv

Runs are resumable: dates already in --out are skipped.
"""
from __future__ import annotations

import argparse
import os
import sys

import pandas as pd

from gexlib.core import approx_rate, compute_gex
from gexlib import sources


def _done_dates(out):
    if os.path.exists(out):
        return set(pd.read_csv(out, usecols=["date"])["date"].astype(str))
    return set()


def _append(out, rows):
    if not rows:
        return
    df = pd.DataFrame(rows)
    df.to_csv(out, mode="a", header=not os.path.exists(out), index=False)


def _gex_row(chain, spot, d, args):
    res = compute_gex(chain, spot, d, r=approx_rate(d), q=args.div_yield,
                      max_dte=args.max_dte, min_oi=args.min_oi,
                      include_0dte=args.include_0dte)
    return res.as_dict()


def run_csv(args):
    done = _done_dates(args.out)
    closes = None
    rows = []
    for d, chain in sources.iter_csv_days(args.path, args.root):
        ds = str(d.date())
        if ds in done or d < pd.Timestamp(args.start) or d > pd.Timestamp(args.end):
            continue
        spot = chain["spot"].dropna().median() if "spot" in chain else float("nan")
        if not spot == spot:  # NaN: need an external close
            if closes is None:
                try:
                    closes = sources.spot_closes(args.ticker, args.start, args.end, args.spot_csv)
                except Exception as e:
                    print(f"could not load spot closes for {args.ticker}: {e}", file=sys.stderr)
                    closes = pd.Series(dtype=float)
            if d not in closes.index:
                print(f"{ds}: no spot price, skipped", file=sys.stderr)
                continue
            spot = float(closes.loc[d])
        rows.append(_gex_row(chain, spot, d, args))
        print(f"{ds}  spot={spot:.2f}  flip={rows[-1]['flip']:.2f}  {rows[-1]['regime']}")
        if len(rows) >= 20:
            _append(args.out, rows)
            rows = []
    _append(args.out, rows)


def run_theta(args):
    done = _done_dates(args.out)
    closes = sources.spot_closes(args.ticker, args.start, args.end, args.spot_csv)
    for d in closes.index:
        ds = str(d.date())
        if ds in done:
            continue
        chain = sources.theta_day(args.root, d)
        if chain.empty:
            print(f"{ds}: no chain data", file=sys.stderr)
            continue
        row = _gex_row(chain, float(closes.loc[d]), d, args)
        _append(args.out, [row])
        print(f"{ds}  spot={row['spot']:.2f}  flip={row['flip']:.2f}  {row['regime']}")


def run_squeeze(args):
    df = sources.squeezemetrics(args.start)
    df.to_csv(args.out, index=False)
    print(f"wrote {len(df)} rows to {args.out}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("source", choices=["csv", "theta", "squeeze"])
    p.add_argument("--path", help="csv: file, directory, or glob of chain files")
    p.add_argument("--root", default="SPY", help="option root (SPY, QQQ, SPX, SPXW, NDX...)")
    p.add_argument("--ticker", default="SPY", help="yfinance ticker for spot closes (^GSPC, ^NDX for index roots)")
    p.add_argument("--spot-csv", help="local CSV with date,close instead of yfinance")
    p.add_argument("--start", default="2020-01-01")
    p.add_argument("--end", default=str(pd.Timestamp.today().date()))
    p.add_argument("--max-dte", type=int, default=45)
    p.add_argument("--include-0dte", action="store_true",
                   help="keep contracts expiring on the quote date (dropped by default)")
    p.add_argument("--min-oi", type=int, default=100)
    p.add_argument("--div-yield", type=float, default=0.013)
    p.add_argument("--out", required=True)
    args = p.parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    {"csv": run_csv, "theta": run_theta, "squeeze": run_squeeze}[args.source](args)


if __name__ == "__main__":
    main()
