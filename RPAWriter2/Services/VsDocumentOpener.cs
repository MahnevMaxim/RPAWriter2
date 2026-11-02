using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Opens or activates a document in Visual Studio as an <see cref="IDocumentOpener"/>, so
    /// models can request "open this file" without referencing DTE or the VS document service.
    /// </summary>
    public sealed class VsDocumentOpener : IDocumentOpener
    {
        public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                return;

            await VsDocumentService.OpenOrActivateAsync(dte, path).ConfigureAwait(true);
        }
    }
}
