using System;
using System.Collections.Generic;
using System.Drawing;
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
        /// <summary>True once the window is closing; the game observer stops then.</summary>
        internal bool ShutDown { get => this.shutDown; private set => this.shutDown = value; }
        private bool shutDown;

        private Control contentHost;
        private UserControl current;
        private string windowTitle;

        // One scaler per view, created the first time that view is shown after the window has
        // loaded - by then it has its final unscaled (but DPI-adjusted) layout to measure from.
        private readonly Dictionary<UserControl, ViewScaler> scalers = new Dictionary<UserControl, ViewScaler>();
        private bool layoutReady;

        // How far the window's own layout has been scaled (DPI auto-scaling), relative to its design.
        // Read back from the scaling itself, since afterwards AutoScaleFactor reports 1 again.
        private SizeF windowScale = new SizeF(1f, 1f);

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
                scaler = new ViewScaler(this.current, this.DesignScale(this.current));
                this.scalers[this.current] = scaler;
            }
            scaler.ScaleTo(this.contentHost.ClientSize);
        }

        // A view put into the content panel after the window's own DPI auto-scaling keeps its
        // designed pixel layout (an inheriting view is only scaled along with a parent that is
        // being scaled), while its point-sized fonts already render at the screen's DPI. Unless
        // the view auto-scaled itself, its layout lags its text by the window's scale factor.
        internal SizeF DesignScale(UserControl view) =>
            view.AutoScaleMode == AutoScaleMode.Font || view.AutoScaleMode == AutoScaleMode.Dpi
                ? new SizeF(1f, 1f)
                : this.windowScale;

        /// <summary>Notes the window's scale factor on top of scaling it.</summary>
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            this.windowScale = new SizeF(this.windowScale.Width * factor.Width, this.windowScale.Height * factor.Height);
            base.ScaleControl(factor, specified);
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

        #region Navigation history
        // The screens visited, each as the action that shows it again, and the one shown now.
        private readonly List<KeyValuePair<string, Action>> history = new List<KeyValuePair<string, Action>>();
        private int historyPosition = -1;
        private ToolStripItem backButton;
        private ToolStripItem forwardButton;

        /// <summary>True if there is a screen to go back to.</summary>
        internal bool CanGoBack => this.historyPosition > 0;
        /// <summary>True if there is a screen to go forward to.</summary>
        internal bool CanGoForward => this.historyPosition < this.history.Count - 1;

        /// <summary>
        /// Puts back and forward arrows at the start of <paramref name="menu"/>, so the window
        /// doesn't grow; Alt+Left and Alt+Right do the same.
        /// </summary>
        protected void AddNavigationButtons(MenuStrip menu)
        {
            this.backButton = new ToolStripMenuItem("◀") { Name = "backToolStripMenuItem", ToolTipText = "Zurück (Alt+Links)" };
            this.backButton.Click += (s, e) => this.GoBack();
            this.forwardButton = new ToolStripMenuItem("▶") { Name = "forwardToolStripMenuItem", ToolTipText = "Vor (Alt+Rechts)" };
            this.forwardButton.Click += (s, e) => this.GoForward();
            menu.Items.Insert(0, this.forwardButton);
            menu.Items.Insert(0, this.backButton);
            menu.ShowItemToolTips = true;
            this.UpdateNavigationButtons();
        }

        /// <summary>
        /// Shows a screen by running <paramref name="show"/> and notes it in the history, dropping
        /// whatever lay ahead of the current screen. <paramref name="key"/> tells screens apart:
        /// the same key twice in a row stays one entry.
        /// </summary>
        protected void Navigate(string key, Action show)
        {
            show();
            this.RecordNavigation(key, show);
        }

        /// <summary>
        /// Notes a screen that is already shown (e.g. a club picked inside a view) in the history;
        /// <paramref name="show"/> brings it back later.
        /// </summary>
        protected void RecordNavigation(string key, Action show)
        {
            if (this.historyPosition >= 0 && this.history[this.historyPosition].Key == key)
            {
                this.history[this.historyPosition] = new KeyValuePair<string, Action>(key, show);
                return;
            }
            this.history.RemoveRange(this.historyPosition + 1, this.history.Count - this.historyPosition - 1);
            this.history.Add(new KeyValuePair<string, Action>(key, show));
            this.historyPosition = this.history.Count - 1;
            this.UpdateNavigationButtons();
        }

        /// <summary>Shows the screen before the current one again; false if there is none.</summary>
        internal bool GoBack() => this.GoTo(this.historyPosition - 1);

        /// <summary>Shows the screen gone back from again; false if there is none.</summary>
        internal bool GoForward() => this.GoTo(this.historyPosition + 1);

        private bool GoTo(int position)
        {
            if (position < 0 || position >= this.history.Count)
                return false;
            this.historyPosition = position;
            this.UpdateNavigationButtons();
            this.history[position].Value();
            return true;
        }

        private void UpdateNavigationButtons()
        {
            if (this.backButton != null)
                this.backButton.Enabled = this.CanGoBack;
            if (this.forwardButton != null)
                this.forwardButton.Enabled = this.CanGoForward;
        }

        /// <summary>Alt+Left and Alt+Right go back and forward, wherever the focus is.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (this.backButton != null && keyData == (Keys.Alt | Keys.Left))
            {
                this.GoBack();
                return true;
            }
            if (this.forwardButton != null && keyData == (Keys.Alt | Keys.Right))
            {
                this.GoForward();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        #endregion

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
