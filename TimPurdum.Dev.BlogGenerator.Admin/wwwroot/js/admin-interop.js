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
// Why the editor is wrapped rather than used directly
// ---------------------------------------------------------------------------
//
// Toast UI's WYSIWYG mode is backed by ProseMirror, whose schema has no node for arbitrary
// raw HTML. Any HTML a post embeds is therefore *discarded* the moment WYSIWYG converts the
// document back to markdown — silently, and with no undo path. Measured on this site's own
// content, a post built out of styled <div> cards lost 4,683 of 14,405 characters (every
// <div> wrapper, every inline style attribute, and the whole <style> block) after a single
// keystroke in WYSIWYG mode.
//
// Configuring the problem away isn't possible: `customHTMLRenderer` can round-trip a <div>
// but reorders its attributes and collapses its newlines, and `htmlSchemaMap` doesn't apply
// to markdown-sourced HTML at all. The only construct Toast UI round-trips byte-for-byte is
// the fenced code block.
//
// So AdminRawHtml below brackets WYSIWYG with a transform: every raw-HTML region is swapped
// for a lossless carrier on the way in and restored verbatim on the way out, with the real
// markdown held outside the editor as the authoritative copy. Regions are located with
// ToastMark's own CommonMark parse (via `sourcepos`), not a hand-rolled HTML scanner, so
// "what counts as raw HTML" always matches what the renderer thinks.
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
// AdminRawHtml — raw-HTML preservation across the WYSIWYG round-trip.
//
// protect()  markdown  ->  WYSIWYG-safe markdown  (HTML swapped for carriers)
// restore()  WYSIWYG-safe markdown  ->  markdown  (carriers swapped back)
//
// Block-level HTML becomes a fenced code block tagged `raw-html`, which keeps the markup
// visible and editable as source. Inline HTML becomes a short opaque token backed by a side
// table, because an inline run has no block to hold it. Deleting a carrier deletes the HTML
// it stands for — that is the intended behaviour, not a failure mode.
// ---------------------------------------------------------------------------
var AdminRawHtml = (function () {
    var FENCE_INFO = "raw-html";
    var TOKEN_RE = /`\[\[raw-html:(\d+)\]\]`/g;

    // Byte offset of the first character of each line, so ToastMark's 1-based [line, col]
    // positions can be turned into string indices. Works for LF and CRLF alike: a CR stays
    // inside the preceding line's content, which is exactly how the parser counts columns.
    function lineStarts(text) {
        var starts = [0];
        for (var i = 0; i < text.length; i++) {
            if (text.charCodeAt(i) === 10) { starts.push(i + 1); }
        }
        return starts;
    }

    // sourcepos is [[startLine, startCol], [endLine, endCol]] — 1-based, end inclusive.
    function toRange(starts, sp) {
        return { start: starts[sp[0][0] - 1] + sp[0][1] - 1, end: starts[sp[1][0] - 1] + sp[1][1] };
    }

    // Parse `markdown` purely to observe node positions. Toast UI has no standalone parser
    // export, so we drive a throwaway hidden editor and let its render pass call us back;
    // the renderers return nothing, since the HTML output is discarded. ~3ms on a 14 KB post,
    // and this only runs on mode switches and idle debounces, never per keystroke.
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
            // A parse failure must never cost the user their content: callers treat an empty
            // region list as "nothing to protect", which leaves the markdown untouched.
            console.error("Raw-HTML scan failed; leaving the document unprotected:", e);
        } finally {
            host.remove();
        }
    }

    // Every raw-HTML region ToastMark finds, in document order.
    function findRawHtml(markdown) {
        var found = [];
        parseNodes(markdown, {
            htmlBlock: function (node) {
                found.push({ kind: "block", sp: node.sourcepos, literal: node.literal });
                return [{ type: "html", content: "" }];
            },
            htmlInline: function (node) {
                found.push({ kind: "inline", sp: node.sourcepos, literal: node.literal });
                return [{ type: "html", content: "" }];
            }
        });
        return found;
    }

    // A fence has to be longer than the longest backtick run in its payload, or the payload
    // could close it early. Counting runs anywhere (not just at line starts) is stricter than
    // CommonMark requires and costs nothing.
    function fenceFor(literal) {
        var longest = 0, run = 0;
        for (var i = 0; i < literal.length; i++) {
            if (literal.charAt(i) === "`") { run++; if (run > longest) { longest = run; } }
            else { run = 0; }
        }
        return new Array(Math.max(3, longest + 1) + 1).join("`");
    }

    function protect(markdown) {
        var regions = findRawHtml(markdown);
        if (!regions.length) { return { text: markdown, tokens: [] }; }

        var starts = lineStarts(markdown);
        var tokens = [];
        var edits = regions.map(function (region) {
            var range = toRange(starts, region.sp);
            var replacement;
            if (region.kind === "block") {
                var fence = fenceFor(region.literal);
                replacement = fence + FENCE_INFO + "\n" + region.literal + "\n" + fence;
            } else {
                replacement = "`[[raw-html:" + tokens.length + "]]`";
                tokens.push(region.literal);
            }
            return { start: range.start, end: range.end, replacement: replacement };
        });

        // Splice back-to-front so earlier offsets stay valid.
        edits.sort(function (a, b) { return b.start - a.start; });
        var text = markdown;
        edits.forEach(function (edit) {
            text = text.slice(0, edit.start) + edit.replacement + text.slice(edit.end);
        });
        return { text: text, tokens: tokens };
    }

    function restore(text, tokens) {
        var lines = text.split("\n");
        var out = [];
        var i = 0;
        while (i < lines.length) {
            var open = /^(`{3,})raw-html[ \t]*$/.exec(lines[i]);
            if (!open) { out.push(lines[i]); i++; continue; }

            var closing = new RegExp("^`{" + open[1].length + ",}[ \\t]*$");
            var j = i + 1;
            while (j < lines.length && !closing.test(lines[j])) { j++; }
            if (j >= lines.length) {
                // Unterminated fence — the user edited the carrier into something we can't read.
                // Emit it verbatim rather than guessing at where the HTML was meant to end.
                out.push(lines[i]); i++; continue;
            }
            out.push(lines.slice(i + 1, j).join("\n"));
            i = j + 1;
        }

        var result = out.join("\n");
        if (tokens.length) {
            result = result.replace(TOKEN_RE, function (whole, index) {
                var n = parseInt(index, 10);
                return n >= 0 && n < tokens.length ? tokens[n] : whole;
            });
        }
        return result;
    }

    // True when the document holds raw HTML, i.e. when WYSIWYG needs the protection above.
    function hasRawHtml(markdown) {
        return findRawHtml(markdown).length > 0;
    }

    // CSS from every <style> element embedded in the document's raw HTML.
    function extractStyleCss(markdown) {
        var css = [];
        findRawHtml(markdown).forEach(function (region) {
            var re = /<style\b[^>]*>([\s\S]*?)<\/style>/gi;
            var match;
            while ((match = re.exec(region.literal)) !== null) { css.push(match[1]); }
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

    return {
        protect: protect,
        restore: restore,
        hasRawHtml: hasRawHtml,
        extractStyleCss: extractStyleCss,
        scopeCss: scopeCss
    };
})();

var adminEditorSeq = 0;

window.adminCreateRichEditor = function (element, initialValue, dotnetRef, options) {
    options = options || {};

    // The authoritative markdown, held outside the editor. In WYSIWYG the editor's own buffer
    // holds the *protected* form, so getMarkdown() there answers a different question than the
    // one C# is asking; everything the .NET side sees comes from here instead.
    var authoritative = initialValue || "";
    var tokens = [];
    // Guards re-entrancy: our own setMarkdown calls raise change/changeMode, which would
    // otherwise be read back as user edits.
    var applying = false;

    // "auto" opens documents containing raw HTML in markdown mode. WYSIWYG can no longer lose
    // that markup, but it can only show it as source, whereas markdown mode's preview pane
    // renders the real thing — which is what someone editing a card-heavy post wants to see.
    var startMode = options.startMode || "auto";
    if (startMode === "auto") {
        startMode = AdminRawHtml.hasRawHtml(authoritative) ? "markdown" : "wysiwyg";
    }

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
        var css = AdminRawHtml.extractStyleCss(authoritative);
        postCssEl.textContent = css
            ? AdminRawHtml.scopeCss(css, "." + scopeClass + " .toastui-editor-contents")
            : "";
    }
    function schedulePostCssSync() {
        if (postCssTimer !== null) { clearTimeout(postCssTimer); }
        postCssTimer = setTimeout(function () { postCssTimer = null; syncPostCss(); }, 400);
    }

    // The mode we believe we're in. Toast UI raises `change` *before* `changeMode` when the user
    // flips modes, and by then it has already converted (and flattened) the buffer — so that
    // change looks exactly like a user edit. Comparing the editor's live mode against this lets
    // the handler tell the two apart: a mismatch means "conversion in flight, ignore it".
    var currentMode = startMode;

    // Load `md` into the editor in whichever representation the current mode needs.
    function applyToEditor(md) {
        applying = true;
        try {
            if (currentMode === "wysiwyg") {
                var protectedForm = AdminRawHtml.protect(md);
                tokens = protectedForm.tokens;
                editor.setMarkdown(protectedForm.text, false);
            } else {
                tokens = [];
                editor.setMarkdown(md, false);
            }
        } finally {
            applying = false;
        }
    }

    var editor = new toastui.Editor({
        el: element,
        initialValue: "",
        initialEditType: startMode,
        previewStyle: "vertical",
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
                // `editor` is still unassigned if anything fires during construction.
                if (applying || !editor) { return; }
                // Raised by a mode conversion rather than by the user — the buffer has already
                // been rewritten by Toast UI and is not a copy we want to trust. changeMode
                // fires next and re-seeds from `authoritative`.
                if (editor.isWysiwygMode() !== (currentMode === "wysiwyg")) { return; }

                authoritative = currentMode === "wysiwyg"
                    ? AdminRawHtml.restore(editor.getMarkdown(), tokens)
                    : editor.getMarkdown();
                schedulePostCssSync();
                try { dotnetRef.invokeMethodAsync("OnEditorChanged", authoritative); }
                catch (e) { /* component disposed mid-edit; ignore */ }
            },
            // Fires after Toast UI has converted the buffer into the new mode — which, going
            // into WYSIWYG, means it has already flattened any raw HTML. Re-seeding from
            // `authoritative` (never from getMarkdown()) is what makes that harmless.
            changeMode: function (mode) {
                if (applying || !editor) { return; }
                if (currentMode === "wysiwyg" && mode !== "wysiwyg") {
                    // Leaving WYSIWYG: the buffer Toast UI just converted is the protected
                    // form, so read it back through restore() to recover the user's edits
                    // before it becomes the source of truth again.
                    authoritative = AdminRawHtml.restore(editor.getMarkdown(), tokens);
                }
                currentMode = mode;
                applyToEditor(authoritative);
            }
        }
    });

    // initialValue is set through applyToEditor rather than the constructor so the protected
    // form is what lands in the buffer when we open straight into WYSIWYG.
    applyToEditor(authoritative);
    syncPostCss();

    return {
        getMarkdown: function () { return authoritative; },
        setMarkdown: function (md) {
            authoritative = md || "";
            applyToEditor(authoritative);
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
