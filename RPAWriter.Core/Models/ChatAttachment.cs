
namespace RPAWriter.Core.Models
{
    /// <summary>
    /// A pending chat attachment, shown as a removable chip above the input box.
    /// Image attachments are forwarded to the provider as multimodal content parts;
    /// text files contribute their content to the prompt on send.
    /// </summary>
    public sealed class ChatAttachment
    {
        /// <summary>Original file name (display only).</summary>
        public string FileName { get; set; } = "";

        /// <summary>True when the attachment is an image sent as a vision part.</summary>
        public bool IsImage { get; set; }

        /// <summary>MIME type for image attachments, e.g. "image/png".</summary>
        public string MediaType { get; set; } = "";

        /// <summary>Base64-encoded bytes for image attachments.</summary>
        public string DataBase64 { get; set; } = "";

        /// <summary>Text content for non-image text files, inserted into the prompt on send.</summary>
        public string TextContent { get; set; } = "";

        /// <summary>Chip label shown in the UI.</summary>
        public string DisplayText => IsImage ? "Image: " + FileName : "File: " + FileName;

        /// <summary>Builds the provider-facing image part for image attachments.</summary>
        public ChatImagePart ToImagePart()
        {
            return new ChatImagePart { MediaType = MediaType, DataBase64 = DataBase64 };
        }
    }
}
