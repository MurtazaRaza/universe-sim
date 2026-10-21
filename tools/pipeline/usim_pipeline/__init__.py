"""Data pipeline of the Universe Simulation (ARCHITECTURE section 4).

Commands: fetch, verify, build, info and reference. Run them through tools/pipeline/usim-pipeline.
"""

__version__ = "0.1.0"


class PipelineError(Exception):
    """A failure reported to the user: invalid input, a failed check or a download problem.

    The command line prints the message and exits with status 1.
    """
