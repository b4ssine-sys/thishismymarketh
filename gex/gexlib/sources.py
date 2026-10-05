"""Data adapters. Each chain loader returns a DataFrame with columns:
date, expiration, strike, right ('C'/'P'), open_interest, and iv or bid/ask.
"""
from __future__ import annotations

import glob
import io
import os

import numpy as np
import pandas as pd

# Column aliases seen in common vendor exports (CBOE DataShop EOD Summary,
# ThetaData CSV, Polygon/Databento flat files, hand-built CSVs).
ALIASES = {
    "date": ["date", "quote_date", "trade_date", "QUOTE_DATE", "tradeDate"],
    "expiration": ["expiration", "expiration_date", "expiry", "exp", "EXPIRE_DATE", "expirDate"],
    "strike": ["strike", "strike_price", "STRIKE"],
    "right": ["right", "option_type", "type", "call_put", "put_call", "cp_flag"],
    "open_interest": ["open_interest", "openinterest", "oi", "OpenInterest", "openInterest"],
    "iv": ["iv", "implied_volatility", "implied_volatility_1545", "impliedVolatility", "IV"],
    "bid": ["bid", "bid_1545", "bid_eod", "close_bid"],
    "ask": ["ask", "ask_1545", "ask_eod", "close_ask"],
    "spot": ["spot", "underlying_price", "underlying_last", "UNDERLYING_LAST",
             "active_underlying_price_1545", "stkPx"],
    "underlying_bid": ["underlying_bid_1545", "underlying_bid_eod"],
    "underlying_ask": ["underlying_ask_1545", "underlying_ask_eod"],
}


def normalize(df: pd.DataFrame) -> pd.DataFrame:
    rename = {}
    lower = {c.lower().strip("[] "): c for c in df.columns}
    for target, names in ALIASES.items():
        for n in names:
            if n.lower() in lower:
                rename[lower[n.lower()]] = target
                break
    df = df.rename(columns=rename)
    missing = {"date", "expiration", "strike", "right", "open_interest"} - set(df.columns)
    if missing:
        raise ValueError(f"chain file missing columns {sorted(missing)}; have {list(df.columns)}")
    df["date"] = pd.to_datetime(df["date"]).dt.normalize()
    df["expiration"] = pd.to_datetime(df["expiration"]).dt.normalize()
    df["right"] = df["right"].astype(str).str.upper().str[0]
    if "spot" not in df and {"underlying_bid", "underlying_ask"} <= set(df.columns):
        df["spot"] = (df["underlying_bid"] + df["underlying_ask"]) / 2.0
    if "spot" in df:
        # DataShop files without a Cboe Global Indices subscription report 0 for index spots.
        df["spot"] = pd.to_numeric(df["spot"], errors="coerce").where(lambda x: x > 0)
    if "iv" in df:
        df["iv"] = pd.to_numeric(df["iv"], errors="coerce")
        df.loc[df["iv"] <= 0, "iv"] = np.nan
    return df


def iter_csv_days(path: str, root: str | None = None):
    """Yield (date, chain_df) from a CSV file, a directory of CSVs, or a glob.
    Files may hold one day each or many days; .csv.gz and .zip both work."""
    if os.path.isdir(path):
        files = sorted(glob.glob(os.path.join(path, "**", "*.csv*"), recursive=True)
                       + glob.glob(os.path.join(path, "**", "*.zip"), recursive=True))
    else:
        files = sorted(glob.glob(path))
    for f in files:
        df = normalize(pd.read_csv(f))
        if root is not None:
            # Match on the underlying first so SPX picks up both SPX and SPXW roots.
            for col in ("underlying_symbol", "symbol", "root"):
                if col in df.columns:
                    df = df[df[col].astype(str).str.upper().isin({root.upper(), "^" + root.upper()})]
                    break
        for d, g in df.groupby("date"):
            yield d, g


# --- ThetaData (Theta Terminal running locally, v2 REST API) ----------------
# Requires a subscription that includes historical options EOD + open interest.
# Endpoint shapes follow ThetaData's v2 docs; check them against your terminal
# version (v3 terminals use different paths) before a long run.
THETA_URL = os.environ.get("THETA_URL", "http://127.0.0.1:25510")


def _theta_bulk(endpoint: str, root: str, ymd: str) -> pd.DataFrame:
    import requests
    rows, url = [], f"{THETA_URL}/v2/bulk_hist/option/{endpoint}"
    params = {"root": root, "exp": 0, "start_date": ymd, "end_date": ymd}
    while url:
        r = requests.get(url, params=params, timeout=120)
        if r.status_code == 472:  # ThetaData "no data" (holiday / weekend)
            return pd.DataFrame()
        r.raise_for_status()
        js = r.json()
        fmt = js["header"]["format"]
        for item in js["response"]:
            c = item["contract"]
            for t in item["ticks"]:
                rec = dict(zip(fmt, t))
                rec.update(expiration=str(c["expiration"]), strike=c["strike"] / 1000.0,
                           right=c["right"])
                rows.append(rec)
        nxt = js["header"].get("next_page")
        url, params = (nxt, None) if nxt and nxt != "null" else (None, None)
    return pd.DataFrame(rows)


def theta_day(root: str, date) -> pd.DataFrame:
    ymd = pd.Timestamp(date).strftime("%Y%m%d")
    eod = _theta_bulk("eod", root, ymd)
    oi = _theta_bulk("open_interest", root, ymd)
    if eod.empty or oi.empty:
        return pd.DataFrame()
    key = ["expiration", "strike", "right"]
    df = eod[key + ["bid", "ask"]].merge(oi[key + ["open_interest"]], on=key)
    df["expiration"] = pd.to_datetime(df["expiration"], format="%Y%m%d")
    df["date"] = pd.Timestamp(date)
    return df


# --- Spot closes ------------------------------------------------------------
def spot_closes(ticker: str, start, end, csv_path: str | None = None) -> pd.Series:
    """Daily closes indexed by date. Uses a local CSV (date, close) if given,
    otherwise yfinance."""
    if csv_path:
        s = pd.read_csv(csv_path)
        s.columns = [c.lower() for c in s.columns]
        return pd.Series(s["close"].values, index=pd.to_datetime(s["date"]).dt.normalize())
    import yfinance as yf
    h = yf.Ticker(ticker).history(start=start, end=pd.Timestamp(end) + pd.Timedelta(days=1),
                                  auto_adjust=False)
    return pd.Series(h["Close"].values, index=h.index.tz_localize(None).normalize())


# --- SqueezeMetrics (free daily SPX GEX number, 2011 onward) -----------------
SQUEEZE_URL = "https://squeezemetrics.com/monitor/static/DIX.csv"


def squeezemetrics(start="2020-01-01") -> pd.DataFrame:
    import requests
    r = requests.get(SQUEEZE_URL, timeout=60, headers={"User-Agent": "Mozilla/5.0"})
    r.raise_for_status()
    df = pd.read_csv(io.StringIO(r.text), parse_dates=["date"])
    return df[df["date"] >= pd.Timestamp(start)].reset_index(drop=True)
