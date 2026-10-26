"""Run the pinned yt-dlp in a cancellable, app-independent POSIX process group."""
import os
import runpy
import signal
import sys

try:
    os.setsid()
except (OSError, AttributeError):
    isolated = False
else:
    isolated = os.getpgrp() == os.getpid()

if isolated:
    def stop_group(_signal, _frame):
        # This group contains only this extractor and its own JS subprocesses.
        os.killpg(os.getpid(), signal.SIGKILL)
    signal.signal(signal.SIGTERM, stop_group)
    signal.signal(signal.SIGINT, stop_group)

target = sys.argv[1]
sys.argv = sys.argv[1:]
runpy.run_path(target, run_name="__main__")
