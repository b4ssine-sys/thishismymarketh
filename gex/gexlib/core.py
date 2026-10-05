"""Core GEX math: Black-Scholes gamma, implied vol inversion, daily GEX profile.

Convention (same as SqueezeMetrics / most retail GEX tools): dealers are long
calls and short puts, so call gamma counts positive and put gamma negative.

    GEX per contract = gamma * OI * 100 * S^2 * 0.01
    (dollar change in dealer delta for a 1% move in the underlying)
"""
from __future__ import annotations

from dataclasses import dataclass, asdict

import numpy as np
from scipy.special import ndtr

SQRT_2PI = np.sqrt(2.0 * np.pi)


def _d1(S, K, T, r, q, iv):
    return (np.log(S / K) + (r - q + 0.5 * iv * iv) * T) / (iv * np.sqrt(T))


def bs_price(S, K, T, r, q, iv, is_call):
    d1 = _d1(S, K, T, r, q, iv)
    d2 = d1 - iv * np.sqrt(T)
    disc_q, disc_r = np.exp(-q * T), np.exp(-r * T)
    call = S * disc_q * ndtr(d1) - K * disc_r * ndtr(d2)
    put = K * disc_r * ndtr(-d2) - S * disc_q * ndtr(-d1)
    return np.where(is_call, call, put)


def bs_gamma(S, K, T, r, q, iv):
    d1 = _d1(S, K, T, r, q, iv)
    return np.exp(-q * T) * np.exp(-0.5 * d1 * d1) / (SQRT_2PI * S * iv * np.sqrt(T))


def implied_vol(price, S, K, T, r, q, is_call, lo=0.005, hi=5.0, iters=80):
    """Vectorized bisection. Returns NaN where price is outside the no-arbitrage band."""
    price = np.asarray(price, float)
    lo_arr = np.full_like(price, lo)
    hi_arr = np.full_like(price, hi)
    p_lo = bs_price(S, K, T, r, q, lo_arr, is_call)
    p_hi = bs_price(S, K, T, r, q, hi_arr, is_call)
    valid = (price > p_lo) & (price < p_hi)
    for _ in range(iters):
        mid = 0.5 * (lo_arr + hi_arr)
        too_high = bs_price(S, K, T, r, q, mid, is_call) > price
        hi_arr = np.where(too_high, mid, hi_arr)
        lo_arr = np.where(too_high, lo_arr, mid)
    out = 0.5 * (lo_arr + hi_arr)
    return np.where(valid, out, np.nan)


@dataclass
class GexResult:
    date: str
    spot: float
    net_gex: float          # $ per 1% move, summed over all strikes
    call_gex: float
    put_gex: float
    flip: float             # spot level where net GEX crosses zero (NaN if none in range)
    call_wall: float        # strike with the largest positive (call) GEX
    put_wall: float         # strike with the largest negative (put) GEX
    regime: str             # "positive" or "negative" gamma at spot
    n_contracts: int

    def as_dict(self):
        return asdict(self)


def compute_gex(chain, spot, date, r=0.04, q=0.013, max_dte=45, min_oi=100,
                min_iv=0.01, grid_pct=0.10, grid_n=401, include_0dte=False):
    """Compute one day's GEX from a chain DataFrame.

    Required columns: expiration (datetime64), strike, right ('C'/'P'),
    open_interest, and either iv or (bid, ask) / mid.
    """
    import pandas as pd

    df = chain.copy()
    d = pd.Timestamp(date)
    # Time to expiry from a 15:45 snapshot to the 4pm close on expiration day.
    # Same-day expiries are dropped by default: they are gone by the next
    # session, which is the session these levels are used for.
    dte = (pd.to_datetime(df["expiration"]) - d).dt.days
    df["T"] = (dte.clip(lower=0) + 15.0 / 1440.0) / 365.0
    min_dte = 0 if include_0dte else 1
    df = df[(dte >= min_dte) & (dte <= max_dte) & (df["open_interest"] >= min_oi)]
    is_call = (df["right"].str.upper().str[0] == "C").to_numpy()
    K = df["strike"].to_numpy(float)
    T = df["T"].to_numpy(float)

    if "iv" in df and df["iv"].notna().any():
        iv = df["iv"].to_numpy(float)
    else:
        mid = df["mid"] if "mid" in df else (df["bid"] + df["ask"]) / 2.0
        iv = implied_vol(mid.to_numpy(float), spot, K, T, r, q, is_call)

    keep = np.isfinite(iv) & (iv >= min_iv)
    K, T, iv, is_call = K[keep], T[keep], iv[keep], is_call[keep]
    oi = df["open_interest"].to_numpy(float)[keep]
    sign = np.where(is_call, 1.0, -1.0)

    if len(K) == 0:
        return GexResult(str(d.date()), spot, np.nan, np.nan, np.nan, np.nan,
                         np.nan, np.nan, "unknown", 0)

    def gex_at(S):
        return sign * bs_gamma(S, K, T, r, q, iv) * oi * 100.0 * S * S * 0.01

    g = gex_at(spot)
    net = g.sum()

    # Walls: aggregate by strike.
    strikes = np.unique(K)
    call_by_k = np.array([g[(K == k) & is_call].sum() for k in strikes])
    put_by_k = np.array([g[(K == k) & ~is_call].sum() for k in strikes])
    call_wall = strikes[np.argmax(call_by_k)] if call_by_k.max() > 0 else np.nan
    put_wall = strikes[np.argmin(put_by_k)] if put_by_k.min() < 0 else np.nan

    # Flip: re-price gamma across hypothetical spots (sticky-strike IV) and
    # find the zero crossing closest to the current spot.
    grid = np.linspace(spot * (1 - grid_pct), spot * (1 + grid_pct), grid_n)
    profile = np.array([gex_at(s).sum() for s in grid])
    flip = np.nan
    cross = np.where(np.diff(np.sign(profile)) != 0)[0]
    if len(cross):
        i = cross[np.argmin(np.abs(grid[cross] - spot))]
        x0, x1, y0, y1 = grid[i], grid[i + 1], profile[i], profile[i + 1]
        flip = x0 - y0 * (x1 - x0) / (y1 - y0)

    return GexResult(
        date=str(d.date()), spot=float(spot), net_gex=float(net),
        call_gex=float(g[is_call].sum()), put_gex=float(g[~is_call].sum()),
        flip=float(flip), call_wall=float(call_wall), put_wall=float(put_wall),
        regime="positive" if net >= 0 else "negative", n_contracts=int(len(K)),
    )


# Approximate 3-month T-bill yields, used when no rate file is supplied.
# Gamma is not very sensitive to r at <=45 DTE; these are close enough.
_RATE_TABLE = [
    ("2020-01-01", 0.015), ("2020-03-16", 0.001), ("2022-03-17", 0.005),
    ("2022-05-05", 0.010), ("2022-06-16", 0.017), ("2022-07-28", 0.025),
    ("2022-09-22", 0.032), ("2022-11-03", 0.040), ("2022-12-15", 0.043),
    ("2023-02-02", 0.046), ("2023-03-23", 0.048), ("2023-05-04", 0.051),
    ("2023-07-27", 0.053), ("2024-09-19", 0.048), ("2024-11-08", 0.045),
    ("2024-12-19", 0.043), ("2025-09-18", 0.040), ("2025-10-30", 0.038),
    ("2025-12-11", 0.036),
]


def approx_rate(date) -> float:
    import pandas as pd
    d = pd.Timestamp(date)
    r = _RATE_TABLE[0][1]
    for start, val in _RATE_TABLE:
        if d >= pd.Timestamp(start):
            r = val
    return r
