"""Synthetic checks: IV inversion round-trips, and a put-heavy-below /
call-heavy-above chain produces a flip between the two clusters."""
import os
import sys
import subprocess

import numpy as np
import pandas as pd

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from gexlib.core import bs_price, compute_gex, implied_vol  # noqa: E402


def synthetic_chain(spot=500.0, date="2024-03-01"):
    d = pd.Timestamp(date)
    rows = []
    for dte in (1, 7, 14, 30):
        exp = d + pd.Timedelta(days=dte)
        for k in np.arange(spot * 0.85, spot * 1.15 + 1, 5.0):
            iv = 0.18 + 0.4 * ((k / spot) - 1) ** 2 - 0.1 * ((k / spot) - 1)  # skew + smile
            for right in "CP":
                # Calls concentrated above spot, puts below, puts heavier overall.
                oi = (4000 if k > spot else 500) if right == "C" else (8000 if k < spot else 500)
                T = (dte + 0.04) / 365
                px = bs_price(spot, k, T, 0.05, 0.013, iv, right == "C")
                rows.append(dict(date=d, expiration=exp, strike=k, right=right,
                                 open_interest=oi, bid=px * 0.99, ask=px * 1.01, true_iv=iv))
    return pd.DataFrame(rows)


def test_iv_roundtrip():
    S, K, T = 500.0, np.array([450, 500, 550.0]), np.array([0.1, 0.1, 0.1])
    iv = np.array([0.25, 0.18, 0.15])
    for is_call in (True, False):
        px = bs_price(S, K, T, 0.05, 0.01, iv, is_call)
        got = implied_vol(px, S, K, T, 0.05, 0.01, np.full(3, is_call))
        assert np.allclose(got, iv, atol=1e-5), got


def test_flip_and_walls():
    spot = 500.0
    res = compute_gex(synthetic_chain(spot), spot, "2024-03-01", r=0.05)
    assert res.n_contracts > 100
    assert np.isfinite(res.flip) and 470 < res.flip < 530, res
    assert res.call_wall > spot and res.put_wall < spot, res
    assert res.put_gex < 0 < res.call_gex


def test_cli_csv_roundtrip(tmp_path):
    chain = synthetic_chain()
    chain["spot"] = 500.0
    src = tmp_path / "chain.csv"
    out = tmp_path / "out.csv"
    chain.drop(columns="true_iv").to_csv(src, index=False)
    cmd = [sys.executable, os.path.join(os.path.dirname(HERE), "build_history.py"), "csv",
           "--path", str(src), "--start", "2024-01-01", "--end", "2024-12-31", "--out", str(out)]
    subprocess.run(cmd, check=True, cwd=os.path.dirname(HERE))
    subprocess.run(cmd, check=True, cwd=os.path.dirname(HERE))  # resume: no duplicate rows
    df = pd.read_csv(out)
    assert len(df) == 1 and df["regime"].iloc[0] in ("positive", "negative")
