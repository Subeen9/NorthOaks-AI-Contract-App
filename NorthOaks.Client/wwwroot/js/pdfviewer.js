let pdfViewerWindow = null;

window.initPdfViewer = function () {
    const iframe = document.getElementById('pdfViewer');
    if (!iframe) {
        return false;
    }

    iframe.onload = function () {
        pdfViewerWindow = iframe.contentWindow;
    };

    return true;
};

window.onPdfViewerLoaded = function () {
    const iframe = document.getElementById('pdfViewer');
    if (iframe && iframe.contentWindow) {
        pdfViewerWindow = iframe.contentWindow;
    }
};

window.pdfViewerClearHighlights = function () {
    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const eventBus = iframeWindow.PDFViewerApplication?.eventBus;

        if (eventBus) {
            eventBus.dispatch('find', {
                source: window,
                type: 'find',
                query: '',
                caseSensitive: false,
                highlightAll: false,
                findPrevious: false
            });
        }

        const iframeDoc = iframeWindow.document;

        const highlightLayers = iframeDoc.querySelectorAll('.highlight-layer');
        highlightLayers.forEach(layer => {
            layer.innerHTML = '';
        });

        const highlights = iframeDoc.querySelectorAll('.pdf-highlight, [data-highlight="true"]');
        highlights.forEach(el => {
            el.remove();
        });

        return true;
    } catch (error) {
        return false;
    }
};

window.pdfViewerGoToPage = function (pageNumber) {
    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const pdfViewer = iframeWindow.PDFViewerApplication?.pdfViewer;

        if (!pdfViewer) {
            return false;
        }

        pdfViewer.currentPageNumber = pageNumber;
        return true;
    } catch (error) {
        return false;
    }
};

window.pdfViewerHighlightText = function (searchText) {
    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const eventBus = iframeWindow.PDFViewerApplication?.eventBus;

        if (!eventBus) {
            return false;
        }

        eventBus.dispatch('find', {
            source: window,
            type: 'find',
            query: searchText,
            caseSensitive: false,
            entireWord: false,
            highlightAll: true,
            findPrevious: false
        });

        return true;
    } catch (error) {
        return false;
    }
};