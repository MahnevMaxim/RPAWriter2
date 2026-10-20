namespace RPAWriter2.Controls
{
    /// <summary>
    /// Severity of a notification banner; drives its accent color and which
    /// action buttons (copy / send to chat / close) are visible.
    /// </summary>
    public enum NotificationKind
    {
        /// <summary>Most verbose level; rendered with a white border and white text.</summary>
        Trace,
        /// <summary>Informational; blue.</summary>
        Info,
        /// <summary>Successful action; green.</summary>
        Success,
        /// <summary>Warning; orange. Shows copy and send-to-chat actions.</summary>
        Warning,
        /// <summary>Error; red. Shows copy and send-to-chat actions.</summary>
        Error
    }
}
