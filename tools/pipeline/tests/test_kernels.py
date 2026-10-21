"""The text kernel parser and its cross-check against CSPICE (ADR-0007). Synthetic kernels only."""

from __future__ import annotations

import pytest

from usim_pipeline import PipelineError
from usim_pipeline.kernels import TextKernel, kernel_pool, kernel_values, parse_at_date, parse_number, ulp_distance

SYNTHETIC = r"""KPL/PCK

Text before the first data section is ignored, even TEST_IGNORED = ( 1 2 3 ).

\begindata

   TEST_SCALAR      = 1.5D0
   TEST_LIST        = ( 1.0, -2.5D+02
                        3E-3   +4.
                        .5 )
   TEST_APPEND      = ( 1 2 )
   TEST_APPEND     += 3
   TEST_STRING      = 'it''s text'
   TEST_DATES       = ( 10, @1972-JAN-1
                        11, @1972-JUL-1 )

\begintext

   TEST_IGNORED_TOO = 99

\begindata
   TEST_SCALAR = 2.25
   TEST_CLOSE  = ( 0.1 0.2 0.3 0.7 1.1 )

\begintext
"""


@pytest.fixture
def kernel(tmp_path):
    path = tmp_path / "synthetic.tpc"
    path.write_text(SYNTHETIC)
    return path


def test_parser_reads_data_sections_only(kernel):
    k = TextKernel(kernel)
    assert "TEST_IGNORED" not in k.variables and "TEST_IGNORED_TOO" not in k.variables
    assert k.text("TEST_SCALAR") == ["2.25"]  # = in a later section replaces
    assert k.text("TEST_LIST") == ["1.0", "-2.5D+02", "3E-3", "+4.", ".5"]
    assert k.text("TEST_APPEND") == ["1", "2", "3"]
    assert k.text("TEST_STRING") == ["'it''s text'"]
    assert k.text("TEST_DATES") == ["10", "@1972-JAN-1", "11", "@1972-JUL-1"]


def test_numbers_are_correctly_rounded():
    assert parse_number("-2.5D+02") == -250.0
    assert parse_number("3.9860043550702266D+05") == float("398600.43550702266")
    assert parse_number("+4.") == 4.0
    with pytest.raises(PipelineError):
        parse_number("1.0X")


def test_at_dates():
    assert parse_at_date("@1972-JAN-1") == (1972, 1, 1)
    assert parse_at_date("@2017-jan-01") == (2017, 1, 1)
    with pytest.raises(PipelineError):
        parse_at_date("@1972-01-01")


def test_cross_check_agrees_with_cspice(kernel):
    k = TextKernel(kernel)
    with kernel_pool(kernel):
        values = kernel_values(k, "TEST_LIST")
        assert values is not None
        assert [v.value for v in values] == [1.0, -250.0, 0.003, 4.0, 0.5]
        assert [v.text for v in values] == ["1.0", "-2.5D+02", "3E-3", "+4.", ".5"]
        dates = kernel_values(k, "TEST_DATES")
        assert dates is not None
        # @-dates as formal calendar seconds past 2000-01-01T12:00:00, as CSPICE stores them.
        assert dates[1].value == dates[1].cspice == -883656000.0
        close = kernel_values(k, "TEST_CLOSE")
        assert close is not None and all(ulp_distance(v.value, v.cspice) <= 16 for v in close)
        assert kernel_values(k, "TEST_MISSING") is None


def test_cross_check_detects_a_parser_cspice_disagreement(kernel):
    k = TextKernel(kernel)
    k.variables["TEST_SCALAR"] = ["2.5"]  # simulate a parser mistake
    with kernel_pool(kernel):
        with pytest.raises(PipelineError, match="CSPICE has"):
            kernel_values(k, "TEST_SCALAR")
        k.variables["TEST_LIST"] = ["1.0"]
        with pytest.raises(PipelineError, match="values in the text"):
            kernel_values(k, "TEST_LIST")
        k.variables["TEST_ONLY_PARSER"] = ["1.0"]
        with pytest.raises(PipelineError, match="defined for the parser only"):
            kernel_values(k, "TEST_ONLY_PARSER")


def test_ulp_distance():
    assert ulp_distance(1.0, 1.0) == 0
    assert ulp_distance(0.0, -0.0) == 0
    import math

    assert ulp_distance(1.0, math.nextafter(1.0, 2.0)) == 1
    assert ulp_distance(-1.0, math.nextafter(-1.0, -2.0)) == 1
    assert ulp_distance(math.ulp(0.0), -math.ulp(0.0)) == 2
