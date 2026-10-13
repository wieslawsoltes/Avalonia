namespace Avalonia.VisualTree
{
    /// <summary>
    /// Interface for controls that are at the root of a hosted visual tree, such as popups.
    /// </summary>
    internal interface IHostedVisualTreeRoot
    {
        /// <summary>
        /// Gets the visual tree host.
        /// </summary>
        /// <value>
        /// The visual tree host.
        /// </value>
        Visual? Host { get; }

        /// <summary>
        /// Gets whether keyboard focus-within and key bindings continue through the host.
        /// </summary>
        bool IsInputRootConnectedToHost => false;
    }
}
