// BlogGenerator Admin — JS interop entry points.
//
// Two globals exposed to Blazor:
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

window.adminCreateRichEditor = function (element, initialValue, dotnetRef, options) {
    options = options || {};
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
