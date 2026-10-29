using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AnstossTrainer.Shared
{
    /// <summary>
    /// Background poller shared by all trainers: once a second it looks for the game among the
    /// running processes, lets the trainer attach to the first candidate that really is its game,
    /// and hands each tick to the trainer while the game runs (or tells it the game is gone).
    /// What counts as the game, how to attach and what to refresh is the trainer's business.
    /// </summary>
    public abstract class GameObserver
    {
        /// <summary>The game process currently attached to, or null.</summary>
        protected Process gameProcess;

        /// <summary>True once the trainer's window is closing; ends the polling loop.</summary>
        protected abstract bool ShutDownRequested { get; }

        /// <summary>The processes worth trying to attach to - a cheap pre-filter, e.g. by window title.</summary>
        internal abstract IEnumerable<Process> GetGameCandidates();

        /// <summary>Attaches to the candidate if it really is a supported game with readable data.</summary>
        protected abstract bool TryAttach(Process candidate);

        /// <summary>One tick while attached: refresh whatever needs refreshing.</summary>
        protected abstract void UpdateGameProcess();

        /// <summary>One tick while no game is attached.</summary>
        protected abstract void ClearGameProcess();

        /// <summary>Polling loop run on the observer thread: finds/tracks the game process and drives view refreshes.</summary>
        internal void Observe()
        {
            try
            {
                while (!this.ShutDownRequested)
                {
                    this.FindGame();

                    if (this.gameProcess != null)
                    {
                        this.UpdateGameProcess();
                    }
                    else
                    {
                        this.ClearGameProcess();
                    }

                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception in Observe()", ex);
            }
        }

        protected void FindGame()
        {
            // Already tracking a live game process - no need to rescan every process on the system.
            if (this.gameProcess != null && !this.gameProcess.HasExited)
                return;

            // The candidate filter is only a cheap pre-filter (window titles are shared by dialogs
            // and other programs of the same install), so TryAttach confirms the real game before
            // latching onto a candidate, instead of trusting whichever one happens to be first.
            foreach (Process candidate in this.GetGameCandidates())
            {
                if (this.TryAttach(candidate))
                {
                    this.gameProcess = candidate;
                    return;
                }
            }
        }

        // Isolates one view's refresh/init from the others - a bad address in one view (e.g. an
        // unmapped field) must not stop the others from ever getting their data.
        protected void RefreshView(string viewName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Warn($"{viewName} konnte nicht aktualisiert werden", ex);
            }
        }
    }
}
