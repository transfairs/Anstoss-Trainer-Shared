using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace AnstossTrainer.Shared
{
    /// <summary>
    /// Main-window base shared by all trainers: swaps views in and out of a content panel, zooms
    /// the active view to the window size (see ViewScaler), keeps the window title in step and
    /// runs the game observer on a background thread. A trainer's own window adds its menu and
    /// views and calls <see cref="InitShell"/> once its designer components exist.
    /// </summary>
    public class TrainerShell : Form
    {
        internal bool ShutDown { get => this.shutDown; private set => this.shutDown = value; }
        private bool shutDown;

        private Control contentHost;
        private UserControl current;
        private string windowTitle;

        // One scaler per view, created the first time that view is shown after the window has
        // loaded - by then it has its final unscaled (but DPI-adjusted) layout to measure from.
        private readonly Dictionary<UserControl, ViewScaler> scalers = new Dictionary<UserControl, ViewScaler>();
        private bool layoutReady;

        /// <summary>
        /// Wires the shell up around the trainer's content panel. Call it from the trainer's
        /// constructor after InitializeComponent, so the panel and the designed title exist.
        /// <paramref name="observe"/> is the game observer's polling loop, started once the window
        /// has loaded and expected to end by itself once <see cref="ShutDown"/> is set.
        /// </summary>
        protected void InitShell(Control contentHost, ThreadStart observe)
        {
            this.contentHost = contentHost;
            this.windowTitle = this.Text;

            this.FormClosing += (s, e) => this.ShutDown = true;
            this.Load += (s, e) => new Thread(observe).Start();
            this.Load += (s, e) => this.EnableScaling();
            this.contentHost.Resize += (s, e) => this.ScaleCurrentScreen();
            this.Disposed += (s, e) =>
            {
                foreach (ViewScaler scaler in this.scalers.Values)
                    scaler.Dispose();
                this.scalers.Clear();
            };
        }

        /// <summary>Makes the window resizable from its designed size upwards, which stays the default and the minimum.</summary>
        protected void EnableScaling()
        {
            this.MinimumSize = this.Size;
            this.layoutReady = true;
            this.ScaleCurrentScreen();
        }

        /// <summary>Zooms the active view to fill the content panel.</summary>
        private void ScaleCurrentScreen()
        {
            if (!this.layoutReady || this.current == null)
                return;

            if (!this.scalers.TryGetValue(this.current, out ViewScaler scaler))
            {
                this.current.PerformLayout();
                scaler = new ViewScaler(this.current);
                this.scalers[this.current] = scaler;
            }
            scaler.ScaleTo(this.contentHost.ClientSize);
        }

        /// <summary>
        /// Fills the content panel with an unscaled placeholder (e.g. "waiting for the game"); the
        /// next ShowScreen puts a real view back.
        /// </summary>
        protected void ShowPlaceholder(Control placeholder, String title = null)
        {
            this.contentHost.SuspendLayout();
            this.contentHost.Controls.Clear();
            this.contentHost.Controls.Add(placeholder);
            this.current = null;
            this.contentHost.ResumeLayout();
            this.Text = this.windowTitle + (title != null ? " :: " + title : "");
        }

        /// <summary>Swaps the content panel to the given view and updates the window title.</summary>
        protected void ShowScreen(UserControl userControl, String title = null)
        {
            if (this.current != userControl)
            {
                this.contentHost.SuspendLayout();
                this.contentHost.Controls.Clear();
                this.contentHost.Controls.Add(userControl);
                this.current = userControl;
                this.contentHost.ResumeLayout();
                this.ScaleCurrentScreen();
            }
            this.Text = this.windowTitle + (title != null ? " :: " + title : "");
        }
    }
}
