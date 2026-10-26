using System;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Thrown when a PIX robot cannot be converted because an activity has neither a PIX → MWF
    /// mapping entry nor a built-in WF snippet. The message names the offending activity so the
    /// host can report a clear, actionable error.
    /// </summary>
    public class PixConversionException : Exception
    {
        public PixConversionException(string message)
            : base(message)
        {
        }

        public PixConversionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
