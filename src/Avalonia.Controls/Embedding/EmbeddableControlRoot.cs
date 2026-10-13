using System;
using System.ComponentModel;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Automation;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Platform;

namespace Avalonia.Controls.Embedding
{
    public class EmbeddableControlRoot : TopLevel, IFocusScope, IDisposable, IHostedVisualTreeRoot
    {
        private Control? _host;
        private bool _restoreHostFocus;

        public EmbeddableControlRoot(ITopLevelImpl impl) : this(impl, null)
        {
        }

        public EmbeddableControlRoot() : this(PlatformManager.CreateEmbeddableTopLevel(), null)
        {
        }

        /// <summary>
        /// Creates a native embedded root connected to a hosting Avalonia control.
        /// </summary>
        /// <param name="host">The control owning the root's logical and input scope.</param>
        /// <remarks>The caller is responsible for attaching the platform handle to its native parent.</remarks>
        public EmbeddableControlRoot(Control host) : this(PlatformManager.CreateEmbeddableTopLevel(), host)
        {
        }

        /// <summary>
        /// Creates an embedded root using a supplied platform implementation and optional host.
        /// </summary>
        /// <param name="impl">The platform implementation owned by this root.</param>
        /// <param name="host">The hosting control, or null for an independently hosted root.</param>
        /// <remarks>
        /// A hosted root inherits resources, styles and data context from its host, and routes
        /// input and keyboard focus through it. Its visibility follows the host's effective
        /// visibility. Disposing the root removes this association.
        /// Native parenting and positioning remain the caller's responsibility.
        /// </remarks>
        public EmbeddableControlRoot(ITopLevelImpl impl, Control? host) : base(impl)
        {
            _host = host;
            host?.LogicalChildren.Add(this);
            if (host is not null)
            {
                host.IsEffectivelyVisibleChanged += OnHostVisibilityChanged;
                SetCurrentValue(IsVisibleProperty, host.IsEffectivelyVisible);
                AddHandler(GotFocusEvent, OnHostedFocusChanged, handledEventsToo: true);
                AddHandler(LostFocusEvent, OnHostedFocusChanged, handledEventsToo: true);
            }
        }

        /// <summary>Gets the hosting control, or null for an independent or disposed root.</summary>
        public Control? Host => _host;

        Visual? IHostedVisualTreeRoot.Host => _host;
        bool IHostedVisualTreeRoot.IsInputRootConnectedToHost => _host is not null;
        internal override Interactive? InteractiveParent => _host ?? base.InteractiveParent;
        protected override IStyleHost? StylingParent => _host ?? base.StylingParent;

        protected bool EnforceClientSize { get; set; } = true;

        public void Prepare()
        {
            EnsureInitialized();
            ApplyTemplate();
            LayoutManager.ExecuteInitialLayoutPass();
        }

        public new void StartRendering() => base.StartRendering();

        public new void StopRendering() => base.StopRendering();
        
        private void EnsureInitialized()
        {
            if (!this.IsInitialized)
            {
                var init = (ISupportInitialize)this;
                init.BeginInit();
                init.EndInit();
            }
        }
        
        protected override Size MeasureOverride(Size availableSize)
        {
            if (EnforceClientSize)
                availableSize = PlatformImpl?.ClientSize ?? default(Size);
            var rv = base.MeasureOverride(availableSize);
            if (EnforceClientSize)
                return availableSize;
            return rv;
        }

        protected override Type StyleKeyOverride => typeof(EmbeddableControlRoot);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            EnableVisualLayerManagerLayers();
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new EmbeddableControlRootAutomationPeer(this);
        }

        private void OnHostedFocusChanged(object? sender, FocusChangedEventArgs e)
        {
            if (e.NewFocusedElement is Visual next)
                _restoreHostFocus = GetTopLevel(next) == this;
            // A native LostFocus can precede Closed. Keep the saved host scope in that case.
        }

        private void RestoreHostFocus()
        {
            if (_host is not null && _restoreHostFocus &&
                (FocusManager?.GetFocusedElement() is null || IsKeyboardFocusWithin))
                FocusManager?.Focus(null);
            _restoreHostFocus = false;
        }

        private void OnHostVisibilityChanged(object? sender, EventArgs e)
        {
            if (_host is not null)
                SetCurrentValue(IsVisibleProperty, _host.IsEffectivelyVisible);
        }

        private protected override void HandleClosed()
        {
            RestoreHostFocus();
            // Remove ownership before base teardown so removal cannot reattach a closed root.
            if (_host is not null)
                _host.IsEffectivelyVisibleChanged -= OnHostVisibilityChanged;
            _host?.LogicalChildren.Remove(this);
            base.HandleClosed();
            _host = null;
        }

        public void Dispose()
        {
            RestoreHostFocus();
            PlatformImpl?.Dispose();
            EnsureClosed();
        }
    }
}
