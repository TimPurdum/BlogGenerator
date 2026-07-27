// BlogGenerator Admin — JS interop entry points.
//
// Runs one thing at load time (the SPA-fallback restore, below), then exposes two globals
// to Blazor:
//
//   adminCreateRichEditor(element, initialValue, dotnetRef, options)
//     Creates a Toast UI Editor inside `element`, returns a small handle the C# wrapper
//     calls methods on (getMarkdown / setMarkdown / destroy). Change events fire back into
//     the .NET MarkdownEditor component via dotnetRef.invokeMethodAsync("OnEditorChanged", ...).
//     Requires toastui-editor-all.min.js to be loaded before this script (the bundled build
//     includes ProseMirror; the slim one does not).
//
//   adminResizeImage(bytes, maxWidth, quality)
//     Resizes a raw image byte array via an in-memory canvas and returns a Uint8Array of
//     JPEG-encoded bytes. The C# ImageUploadService hands the result to the GitHub Contents
//     API — resizing in the browser keeps repo bandwidth small and content lean.
//
// ---------------------------------------------------------------------------
// Why the editor is markdown-only
// ---------------------------------------------------------------------------
//
// The editor deliberately runs in markdown mode with a live preview pane, and Toast UI's
// WYSIWYG mode is switched off entirely (`hideModeSwitch`). WYSIWYG is backed by ProseMirror,
// whose schema has no node for arbitrary raw HTML, so any HTML a post embeds is discarded the
// moment it converts back to markdown. Measured on real content here, a post built out of
// styled <div> cards lost 4,683 of 14,405 characters after a single keystroke in WYSIWYG.
//
// That was survivable — an earlier revision bracketed WYSIWYG with a protection transform
// that swapped each HTML region for a lossless carrier and restored it on the way out. But
// the two-pane markdown editor already delivers what WYSIWYG is for (seeing rendered output
// while you type), does it without flattening HTML, and unlike WYSIWYG it returns the
// document byte-for-byte — no canonicalizing `-` bullets to `*`, `---` to `***`, or CRLF to
// LF on every save. Keeping both modes meant carrying ~200 lines of state machinery, tied to
// Toast UI internals, purely to stop one of them destroying content.
//
// The one thing WYSIWYG did better was paste: it converts pasted rich HTML into markdown,
// where the markdown pane would take the clipboard's plain-text flavour and lose every
// heading, link and list. That capability is kept below, without the mode — see the paste
// handler in adminCreateRichEditor.
//
// Separately, Toast UI's sanitizer strips <style> elements from the preview pane, which is
// why class-based cards render unstyled there. AdminRawHtml re-injects that CSS scoped to
// the preview so it cannot leak out and restyle the admin's own chrome.

// ---------------------------------------------------------------------------
// SPA-fallback restore.
//
// Static hosts (GitHub Pages in particular) serve a 404 page for any path that isn't a real
// file, so a deep link like /admin/edit/post/my-slug never reaches index.html on its own. The
// consumer site's 404 handler stashes the requested path under ADMIN_SPA_KEY and bounces the
// browser to the admin's mount point; this restores the URL so Blazor's router boots straight
// to the right page. See "Hosting under a sub-path" in the ReadMe for the 404-side snippet.
//
// This must run before blazor.webassembly.js — the documented script order puts it there. It is
// deliberately synchronous and top-level, not wrapped in a DOMContentLoaded handler, because the
// router reads location during boot.
//
// The mount point comes from document.baseURI (i.e. the page's <base href>), so this works at
// "/admin/", at the site root, or at any other prefix without per-site configuration.
// ---------------------------------------------------------------------------
var ADMIN_SPA_KEY = "admin.spa.redirect";

(function restoreSpaPath() {
    var saved;
    try {
        saved = sessionStorage.getItem(ADMIN_SPA_KEY);
        if (!saved) { return; }
        sessionStorage.removeItem(ADMIN_SPA_KEY);
    } catch (e) {
        // Private mode / storage disabled. The bounce still lands on the dashboard.
        return;
    }

    // Only honour paths inside our own mount point. sessionStorage is writable by anything else
    // served from this origin, so treat the stashed value as untrusted input: a value like
    // "//evil.example.com/x" or a path outside the base would otherwise let another page on the
    // origin steer the admin's initial route.
    var basePath;
    try {
        basePath = new URL(document.baseURI).pathname;
    } catch (e) {
        return;
    }
    if (basePath.charAt(basePath.length - 1) !== "/") { basePath += "/"; }

    // saved must be a root-relative path (single leading slash) under basePath. Reject a second
    // leading "/" or "\" — URL parsing folds "/\" into "//", so both forms would resolve to a
    // cross-origin URL. Compare against basePath without its trailing slash too, so "/admin" is
    // accepted alongside "/admin/...".
    var second = saved.charAt(1);
    if (saved.charAt(0) !== "/" || second === "/" || second === "\\") { return; }
    var baseNoSlash = basePath.slice(0, -1);
    if (saved !== baseNoSlash && saved.indexOf(basePath) !== 0) { return; }

    if (saved !== location.pathname + location.search + location.hash) {
        // Belt and braces: replaceState throws SecurityError on a cross-origin result. The checks
        // above should make that unreachable, but a thrown error here would abort the script and
        // take the interop globals below down with it.
        try { history.replaceState(null, "", saved); } catch (e) { /* keep the default route */ }
    }
})();

// ---------------------------------------------------------------------------
// AdminRawHtml — helpers for the raw HTML that markdown posts embed.
//
// Only one job remains here: post-authored <style> blocks. Toast UI's sanitizer strips
// <style> elements out of the preview pane, so cards built on CSS classes render unstyled.
// We pull that CSS out of the document ourselves and re-inject it, rewritten so it can only
// apply inside the preview.
//
// Finding the <style> blocks goes through ToastMark's own CommonMark parse rather than a
// regex over the source. That distinction matters: a post *documenting* CSS inside a fenced
// code block must not have that CSS actually applied to the preview, and the parser is what
// tells the two apart.
// ---------------------------------------------------------------------------
var AdminRawHtml = (function () {

    // Parse `markdown` purely to observe its nodes. Toast UI has no standalone parser export,
    // so we drive a throwaway hidden editor and let its render pass call us back; the
    // renderers return nothing, since the HTML output is discarded.
    function parseNodes(markdown, renderers) {
        var host = document.createElement("div");
        host.style.display = "none";
        document.body.appendChild(host);
        try {
            new toastui.Editor({
                el: host,
                initialValue: markdown,
                initialEditType: "markdown",
                previewStyle: "vertical",
                height: "50px",
                usageStatistics: false,
                customHTMLRenderer: renderers
            }).destroy();
        } catch (e) {
            // Never let a parse failure cost the user anything: callers treat an empty result
            // as "no embedded CSS", which just leaves the preview unstyled.
            console.error("Raw-HTML scan failed:", e);
        } finally {
            host.remove();
        }
    }

    // The literal text of every raw-HTML region in the document, block and inline.
    function findRawHtml(markdown) {
        var found = [];
        parseNodes(markdown, {
            htmlBlock: function (node) { found.push(node.literal); return [{ type: "html", content: "" }]; },
            htmlInline: function (node) { found.push(node.literal); return [{ type: "html", content: "" }]; }
        });
        return found;
    }

    // CSS from every <style> element embedded in the document's raw HTML.
    function extractStyleCss(markdown) {
        var css = [];
        findRawHtml(markdown).forEach(function (literal) {
            var re = /<style\b[^>]*>([\s\S]*?)<\/style>/gi;
            var match;
            while ((match = re.exec(literal)) !== null) { css.push(match[1]); }
        });
        return css.join("\n");
    }

    // Rewrite `css` so every selector is confined to `prefix`. Parsing goes through the
    // browser's own CSSOM — a real CSS parser, and one that drops anything malformed for us.
    // Scoping is what makes injecting post CSS safe: a post carrying `body { display: none }`
    // becomes `<prefix> body { ... }`, which matches nothing outside the preview pane.
    function scopeCss(css, prefix) {
        var doc = document.implementation.createHTMLDocument("");
        var holder = doc.createElement("style");
        holder.textContent = css;
        doc.head.appendChild(holder);
        if (!holder.sheet) { return ""; }
        return Array.prototype.map.call(holder.sheet.cssRules, function (rule) {
            return scopeRule(rule, prefix);
        }).filter(Boolean).join("\n");
    }

    function scopeRule(rule, prefix) {
        if (rule.type === CSSRule.STYLE_RULE) {
            var selectors = rule.selectorText.split(",").map(function (sel) {
                return prefix + " " + sel.trim();
            });
            return selectors.join(", ") + " { " + rule.style.cssText + " }";
        }
        if (rule.type === CSSRule.MEDIA_RULE || rule.type === CSSRule.SUPPORTS_RULE) {
            var inner = Array.prototype.map.call(rule.cssRules, function (child) {
                return scopeRule(child, prefix);
            }).filter(Boolean).join("\n");
            var at = rule.type === CSSRule.MEDIA_RULE ? "@media " : "@supports ";
            return at + rule.conditionText + " { " + inner + " }";
        }
        if (rule.type === CSSRule.KEYFRAMES_RULE || rule.type === CSSRule.FONT_FACE_RULE) {
            // No selectors to confine, and both are inert until something references them.
            return rule.cssText;
        }
        // Everything else — @import above all — is dropped rather than scoped, so preview
        // rendering can't be made to reach the network.
        return "";
    }

    // Convert pasted rich HTML into markdown, so dropping WYSIWYG doesn't cost us paste
    // fidelity. There's no standalone converter in the public API, so we borrow one: a
    // throwaway WYSIWYG instance parses the HTML into its ProseMirror document and hands
    // back the markdown serialization. The instance is created and destroyed inside this
    // call, so no WYSIWYG surface is ever shown to the user.
    //
    // setHTML runs the input through Toast UI's sanitizer on the way in.
    function htmlToMarkdown(html) {
        var host = document.createElement("div");
        host.style.display = "none";
        document.body.appendChild(host);
        try {
            var converter = new toastui.Editor({
                el: host,
                initialEditType: "wysiwyg",
                height: "50px",
                usageStatistics: false
            });
            converter.setHTML(html, false);
            var markdown = converter.getMarkdown();
            converter.destroy();
            return markdown;
        } catch (e) {
            console.error("Paste conversion failed; falling back to plain text:", e);
            return null;
        } finally {
            host.remove();
        }
    }

    return {
        extractStyleCss: extractStyleCss,
        scopeCss: scopeCss,
        htmlToMarkdown: htmlToMarkdown
    };
})();

var adminEditorSeq = 0;

window.adminCreateRichEditor = function (element, initialValue, dotnetRef, options) {
    options = options || {};

    // Guards re-entrancy: our own setMarkdown calls raise change, which would otherwise be
    // read back as a user edit and echoed to .NET.
    var applying = false;

    function blobToBase64(blob) {
        return blob.arrayBuffer().then(function (buf) {
            var bytes = new Uint8Array(buf);
            var binary = "";
            for (var i = 0; i < bytes.length; i++) { binary += String.fromCharCode(bytes[i]); }
            return btoa(binary);
        });
    }

    // Post CSS is injected under a per-editor class so two editors on one page can't collide,
    // and so nothing here can reach the admin's own chrome.
    var scopeClass = "admin-editor-scope-" + (++adminEditorSeq);
    element.classList.add(scopeClass);
    var postCssEl = document.createElement("style");
    postCssEl.setAttribute("data-admin-post-css", scopeClass);
    document.head.appendChild(postCssEl);

    var postCssTimer = null;
    function syncPostCss() {
        var css = AdminRawHtml.extractStyleCss(editor.getMarkdown());
        postCssEl.textContent = css
            ? AdminRawHtml.scopeCss(css, "." + scopeClass + " .toastui-editor-contents")
            : "";
    }
    function schedulePostCssSync() {
        if (postCssTimer !== null) { clearTimeout(postCssTimer); }
        postCssTimer = setTimeout(function () { postCssTimer = null; syncPostCss(); }, 400);
    }

    var editor = new toastui.Editor({
        el: element,
        initialValue: initialValue || "",
        initialEditType: "markdown",
        previewStyle: "vertical",
        // WYSIWYG flattens embedded raw HTML and canonicalizes the whole document on the way
        // back out. The preview pane covers what it was for; the mode itself is not offered.
        hideModeSwitch: true,
        height: options.height || "32rem",
        usageStatistics: false,
        toolbarItems: [
            ["heading", "bold", "italic", "strike"],
            ["hr", "quote"],
            ["ul", "ol", "task"],
            ["table", "image", "link"],
            ["code", "codeblock"]
        ],
        hooks: {
            addImageBlobHook: function (blob, callback) {
                blobToBase64(blob).then(function (b64) {
                    return dotnetRef.invokeMethodAsync("UploadEditorImageAsync", b64, blob.name || "upload.jpg");
                }).then(function (url) {
                    callback(url, "");
                }).catch(function (e) {
                    console.error("Editor image upload failed:", e);
                    callback("", "");
                });
            }
        },
        events: {
            change: function () {
                if (applying || !editor) { return; }
                schedulePostCssSync();
                try { dotnetRef.invokeMethodAsync("OnEditorChanged", editor.getMarkdown()); }
                catch (e) { /* component disposed mid-edit; ignore */ }
            }
        }
    });

    // Rich paste. The markdown pane would otherwise take the clipboard's text/plain flavour,
    // which throws away every heading, link, emphasis and list item from anything copied out
    // of a web page or document. Convert the text/html flavour instead.
    //
    // Two cases deliberately fall through to Toast UI's own handling:
    //   * No text/html at all — a plain-text copy, or the browser's paste-as-plain-text. The
    //     text is inserted verbatim, which is what a markdown source pane should do.
    //   * HTML carrying data-pm-slice — ProseMirror stamps that on its own clipboard writes,
    //     so this is text copied out of this very editor. Round-tripping markdown source
    //     through an HTML conversion would mangle it.
    // Listen on the host in the capture phase, not on the ProseMirror node itself. At the
    // target element, capture and bubble listeners both run in registration order — and
    // ProseMirror registered first, so a listener there fires *after* it has already inserted
    // the plain-text flavour, and preventDefault comes too late to stop it. Capturing on an
    // ancestor is what gets us in front of it; stopPropagation then keeps it out entirely.
    element.addEventListener("paste", function (e) {
        var pane = element.querySelector(".toastui-editor-md-container .ProseMirror");
        if (!pane || !e.target || (e.target !== pane && !pane.contains(e.target))) { return; }
        if (!e.clipboardData) { return; }

        var html = e.clipboardData.getData("text/html");
        if (!html || html.indexOf("data-pm-slice") !== -1) { return; }

        var markdown = AdminRawHtml.htmlToMarkdown(html);
        if (markdown === null || markdown === "") { return; }

        e.preventDefault();
        e.stopPropagation();
        editor.replaceSelection(markdown);
    }, true);

    syncPostCss();

    return {
        getMarkdown: function () { return editor.getMarkdown(); },
        setMarkdown: function (md) {
            applying = true;
            try { editor.setMarkdown(md || "", false); }
            finally { applying = false; }
            syncPostCss();
        },
        destroy: function () {
            if (postCssTimer !== null) { clearTimeout(postCssTimer); }
            postCssEl.remove();
            editor.destroy();
        }
    };
};

window.adminResizeImage = function (bytes, maxWidth, quality) {
    return new Promise(function (resolve, reject) {
        try {
            var blob = new Blob([bytes]);
            var url = URL.createObjectURL(blob);
            var img = new Image();
            img.onload = function () {
                try {
                    var ratio = Math.min(1, maxWidth / img.naturalWidth);
                    var w = Math.round(img.naturalWidth * ratio);
                    var h = Math.round(img.naturalHeight * ratio);
                    var canvas = document.createElement("canvas");
                    canvas.width = w;
                    canvas.height = h;
                    var ctx = canvas.getContext("2d");
                    if (!ctx) { reject("could not get 2d canvas context"); return; }
                    // White background so PNG transparency doesn't go black when we re-encode to JPEG.
                    ctx.fillStyle = "#FFFFFF";
                    ctx.fillRect(0, 0, w, h);
                    ctx.drawImage(img, 0, 0, w, h);
                    canvas.toBlob(function (out) {
                        if (!out) { reject("canvas.toBlob produced no output"); return; }
                        out.arrayBuffer().then(function (buf) {
                            resolve(new Uint8Array(buf));
                        }, reject);
                    }, "image/jpeg", quality);
                } catch (e) {
                    reject(e && e.message ? e.message : String(e));
                } finally {
                    URL.revokeObjectURL(url);
                }
            };
            img.onerror = function () {
                URL.revokeObjectURL(url);
                reject("could not decode the uploaded file as an image");
            };
            img.src = url;
        } catch (e) {
            reject(e && e.message ? e.message : String(e));
        }
    });
};
