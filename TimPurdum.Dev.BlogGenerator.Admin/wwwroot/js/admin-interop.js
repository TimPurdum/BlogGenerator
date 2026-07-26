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

window.adminCreateRichEditor = function (element, initialValue, dotnetRef, options) {
    options = options || {};

    function blobToBase64(blob) {
        return blob.arrayBuffer().then(function (buf) {
            var bytes = new Uint8Array(buf);
            var binary = "";
            for (var i = 0; i < bytes.length; i++) { binary += String.fromCharCode(bytes[i]); }
            return btoa(binary);
        });
    }

    var editor = new toastui.Editor({
        el: element,
        initialValue: initialValue || "",
        initialEditType: options.startMode || "wysiwyg",
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
                try { dotnetRef.invokeMethodAsync("OnEditorChanged", editor.getMarkdown()); }
                catch (e) { /* component disposed mid-edit; ignore */ }
            }
        }
    });
    return {
        getMarkdown: function () { return editor.getMarkdown(); },
        setMarkdown: function (md) { editor.setMarkdown(md || "", false); },
        destroy: function () { editor.destroy(); }
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
