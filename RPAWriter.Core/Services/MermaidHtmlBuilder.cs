using Newtonsoft.Json;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Builds the standalone HTML document used by the WPF Mermaid renderer to draw a diagram
    /// offline. The bundled mermaid.js is loaded by URL (via a WebView2 virtual host mapping)
    /// instead of being inlined, because <c>NavigateToString</c> rejects HTML larger than 2 MB
    /// and the minified mermaid.js exceeds that limit. The diagram source is embedded as a JSON
    /// string so that special characters such as <c>&lt;br/&gt;</c>, <c>--&gt;</c> and emoji survive intact.
    /// </summary>
    public static class MermaidHtmlBuilder
    {
        public static string Build(string mermaidScriptUrl, string code, string theme = "dark")
        {
            var scriptUrl = mermaidScriptUrl ?? string.Empty;
            var diagram = code ?? string.Empty;
            var codeJson = JsonConvert.SerializeObject(diagram);
            var themeJson = JsonConvert.SerializeObject(string.IsNullOrWhiteSpace(theme) ? "dark" : theme);

            return string.Join("\n",
                "<!doctype html>",
                "<html>",
                "<head>",
                "<meta charset=\"utf-8\">",
                "<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden;}#mmd{display:inline-block;}</style>",
                "</head>",
                "<body>",
                "<div id=\"mmd\"></div>",
                "<script src=\"" + scriptUrl + "\"></script>",
                "<script>",
                "(function(){",
                "  function post(obj){ if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(JSON.stringify(obj)); } }",
                "  window.addEventListener('wheel', function(e){ if (!e.ctrlKey && Math.abs(e.deltaY) > 0) { e.preventDefault(); post({ wheel: e.deltaY }); } }, { passive: false });",
                "  mermaid.initialize({ startOnLoad: false, theme: " + themeJson + ", securityLevel: 'loose', flowchart: { useMaxWidth: true } });",
                "  var code = " + codeJson + ";",
                "  mermaid.render('mmd-svg', code).then(function(result){",
                "    document.getElementById('mmd').innerHTML = result.svg;",
                "    var el = document.getElementById('mmd-svg');",
                "    if (el) { var r = el.getBoundingClientRect(); post({ w: Math.ceil(r.width), h: Math.ceil(r.height) }); }",
                "    else { post({ w: 0, h: 0 }); }",
                "  }).catch(function(err){ post({ error: String(err && err.message ? err.message : err) }); });",
                "})();",
                "</script>",
                "</body>",
                "</html>");
        }
    }
}
