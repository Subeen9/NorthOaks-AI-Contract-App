let pdfViewerWindows = {};

window.initPdfViewerWithId = function (viewerId) {
    const iframe = document.getElementById(viewerId);
    if (!iframe) {
        console.error(`PDF viewer iframe '${viewerId}' not found`);
        return false;
    }
    iframe.onload = function () {
        pdfViewerWindows[viewerId] = iframe.contentWindow;
        console.log(`PDF viewer iframe '${viewerId}' loaded`);
    };
    return true;
};

window.initPdfViewer = function () {
    return initPdfViewerWithId('pdfViewer');
};

window.onPdfViewerLoaded = function () {
    const iframe = document.getElementById('pdfViewer');
    if (iframe && iframe.contentWindow) {
        pdfViewerWindows['pdfViewer'] = iframe.contentWindow;
        console.log('PDF viewer window reference updated');
    }
};

window.pdfViewerClearHighlights = function (viewerId = 'pdfViewer') {
    try {
        const iframe = document.getElementById(viewerId);
        if (!iframe || !iframe.contentWindow) {
            console.error(`Cannot clear highlights: iframe '${viewerId}' not accessible`);
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const iframeDoc = iframeWindow.document;

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

        const customHighlights = iframeDoc.querySelectorAll('.custom-text-highlight');
        customHighlights.forEach(el => el.remove());

        const highlightLayers = iframeDoc.querySelectorAll('.highlight-layer');
        highlightLayers.forEach(layer => {
            layer.innerHTML = '';
        });

        const highlights = iframeDoc.querySelectorAll('.pdf-highlight, [data-highlight="true"]');
        highlights.forEach(el => {
            el.remove();
        });

        console.log(`All highlights cleared in '${viewerId}'`);
        return true;
    } catch (error) {
        console.error(`Error clearing highlights in '${viewerId}':`, error);
        return false;
    }
};

window.pdfViewerGoToPage = function (pageNumber, viewerId = 'pdfViewer') {
    try {
        const iframe = document.getElementById(viewerId);
        if (!iframe || !iframe.contentWindow) {
            console.error(`Cannot navigate: iframe '${viewerId}' not accessible`);
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const pdfViewer = iframeWindow.PDFViewerApplication?.pdfViewer;

        if (!pdfViewer) {
            console.error(`PDFViewerApplication.pdfViewer not available in '${viewerId}'`);
            return false;
        }

        pdfViewer.currentPageNumber = pageNumber;
        console.log(`✓ Navigated to page ${pageNumber} in '${viewerId}'`);
        return true;
    } catch (error) {
        console.error(`Error navigating to page in '${viewerId}':`, error);
        return false;
    }
};

window.pdfViewerHighlightText = function (searchText, viewerId = 'pdfViewer') {
    console.log(`=== Starting highlight in '${viewerId}' (${searchText.length} chars) ===`);
    console.log(`Preview: "${searchText.substring(0, 80)}..."`);

    try {
        const iframe = document.getElementById(viewerId);
        if (!iframe || !iframe.contentWindow) {
            console.error(`PDF viewer iframe '${viewerId}' not accessible`);
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const iframeDoc = iframeWindow.document;
        const pdfViewer = iframeWindow.PDFViewerApplication?.pdfViewer;

        if (!pdfViewer) {
            console.error(`PDFViewerApplication not available in '${viewerId}'`);
            return false;
        }

        const currentPageNumber = pdfViewer.currentPageNumber;
        console.log(`Current page: ${currentPageNumber} in '${viewerId}'`);

        return smartParagraphHighlight(iframeDoc, currentPageNumber, searchText, viewerId);

    } catch (error) {
        console.error(`Error in pdfViewerHighlightText for '${viewerId}':`, error);
        console.error('Stack:', error.stack);
        return false;
    }
};

function smartParagraphHighlight(iframeDoc, pageNumber, searchText, viewerId) {
    try {
        const pageDiv = iframeDoc.querySelector(`.page[data-page-number="${pageNumber}"]`);
        if (!pageDiv) {
            console.error(`Page ${pageNumber} not found`);
            return false;
        }

        const textLayer = pageDiv.querySelector('.textLayer');
        if (!textLayer) {
            console.error(`Text layer not found`);
            return false;
        }

        const textSpans = Array.from(textLayer.querySelectorAll('span'));

        // DEBUG: Show all text spans on the page
        console.log(`=== Analyzing ${textSpans.length} spans on page ===`);
        textSpans.slice(0, 15).forEach((span, idx) => {
            console.log(`Span ${idx}: "${span.textContent}" (length: ${span.textContent?.length || 0})`);
        });

        // Extract keywords
        const keywords = searchText
            .toLowerCase()
            .split(/\W+/)
            .filter(w => w.length > 5)
            .slice(0, 5);

        console.log(`Looking for keywords: ${keywords.join(', ')}`);

        // Strategy 1: Look for section headers
        console.log('--- Strategy 1: Looking for section header ---');
        const headerSpan = findSectionHeader(textSpans, searchText);

        if (headerSpan) {
            const headerIndex = textSpans.indexOf(headerSpan);
            console.log(`✓ Found section header at index ${headerIndex}: "${headerSpan.textContent}"`);

            // Highlight header + following paragraph
            const startIdx = headerIndex;
            const endIdx = Math.min(textSpans.length, headerIndex + 25);

            for (let i = startIdx; i < endIdx; i++) {
                createHighlightOverlay(textSpans[i], pageDiv);
            }

            console.log(`✓ Highlighted section header + paragraph (${endIdx - startIdx} spans)`);
            return true;
        }

        console.log('No section header found, trying Strategy 2...');

        // Strategy 2: Keyword matching
        const matchingSpans = [];
        textSpans.forEach(span => {
            const spanText = (span.textContent || '').toLowerCase();
            const matchCount = keywords.filter(kw => spanText.includes(kw)).length;
            if (matchCount > 0) {
                matchingSpans.push({ span, matchCount });
            }
        });

        if (matchingSpans.length === 0) {
            console.log('No matching content found');
            return false;
        }

        matchingSpans.sort((a, b) => b.matchCount - a.matchCount);

        const bestMatch = matchingSpans[0].span;
        const bestIndex = textSpans.indexOf(bestMatch);

        console.log(`Best match at index ${bestIndex}: "${bestMatch.textContent}"`);

        // Look backwards for a section header
        console.log('--- Looking for nearby header ---');
        const nearbyHeader = findNearbyHeader(textSpans, bestIndex);

        if (nearbyHeader) {
            const headerIndex = textSpans.indexOf(nearbyHeader);
            console.log(`✓ Found nearby header at index ${headerIndex}: "${nearbyHeader.textContent}"`);

            const startIdx = headerIndex;
            const endIdx = Math.min(textSpans.length, headerIndex + 25);

            for (let i = startIdx; i < endIdx; i++) {
                createHighlightOverlay(textSpans[i], pageDiv);
            }

            console.log(`✓ Highlighted from header (${endIdx - startIdx} spans)`);
            return true;
        }

        console.log('No nearby header found, using tight range');

        // Strategy 3: Highlight tight range
        const startIdx = Math.max(0, bestIndex - 2);
        const endIdx = Math.min(textSpans.length, bestIndex + 8);

        for (let i = startIdx; i < endIdx; i++) {
            createHighlightOverlay(textSpans[i], pageDiv);
        }

        console.log(`✓ Highlighted around best match (${endIdx - startIdx} spans)`);
        return true;

    } catch (error) {
        console.error('Error highlighting:', error);
        return false;
    }
}

// Helper: Find section header in text spans
function findSectionHeader(textSpans, searchText) {
    const searchLower = searchText.toLowerCase();

    for (const span of textSpans) {
        const text = (span.textContent || '').trim();

        // Skip empty or very short spans
        if (text.length < 3) continue;

        // Check if this looks like a header
        const isHeader =
            // Title Case pattern
            /^[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*$/.test(text) ||
            // All caps (but not single word)
            (/^[A-Z\s]+$/.test(text) && text.split(/\s+/).length > 1) ||
            // Contains common header words
            /^(article|section|clause|provision|schedule|exhibit|appendix)/i.test(text);

        if (isHeader) {
            // Check if header text is in search text
            const headerWords = text.toLowerCase().split(/\s+/);
            const matchCount = headerWords.filter(word =>
                word.length > 3 && searchLower.includes(word)
            ).length;

            if (matchCount >= 1) {
                console.log(`Found matching header: "${text}"`);
                return span;
            }
        }
    }

    return null;
}

// Helper: Find nearby header before the matched span
function findNearbyHeader(textSpans, matchIndex) {
    // Look backwards up to 10 spans
    const searchStart = Math.max(0, matchIndex - 10);

    for (let i = matchIndex - 1; i >= searchStart; i--) {
        const span = textSpans[i];
        const text = (span.textContent || '').trim();

        if (text.length < 3) continue;

        const isHeader =
            /^[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*$/.test(text) ||
            /^[A-Z\s]+$/.test(text) ||
            /^(article|section|clause|provision|schedule|exhibit|appendix)/i.test(text) ||
            /^\d+\.\s+[A-Z]/.test(text); // Numbered sections like "1. Introduction"

        if (isHeader) {
            console.log(`Found nearby header: "${text}"`);
            return span;
        }
    }

    return null;
}

function createHighlightOverlay(span, pageDiv) {
    try {
        const textLayer = pageDiv.querySelector('.textLayer');
        if (!textLayer) {
            console.error('Text layer not found for highlight positioning');
            return;
        }

        const spanRect = span.getBoundingClientRect();
        const pageRect = pageDiv.getBoundingClientRect();

        const left = spanRect.left - pageRect.left;
        const top = spanRect.top - pageRect.top;
        const width = spanRect.width;
        const height = spanRect.height;

        if (width <= 0 || height <= 0) {
            console.warn(`Skipping span with invalid dimensions: w=${width}, h=${height}`);
            return;
        }

        console.log(`Creating highlight: left=${left.toFixed(1)}, top=${top.toFixed(1)}, w=${width.toFixed(1)}, h=${height.toFixed(1)}`);

        const highlight = document.createElement('div');
        highlight.className = 'custom-text-highlight';
        highlight.setAttribute('data-highlight', 'true');
        highlight.style.cssText = `
            position: absolute;
            left: ${left}px;
            top: ${top}px;
            width: ${width}px;
            height: ${height}px;
            background-color: rgba(255, 255, 0, 0.6);
            pointer-events: none;
            z-index: 10;
            mix-blend-mode: multiply;
            border-radius: 2px;
            box-shadow: 0 0 4px rgba(255, 200, 0, 0.8);
            border: 1px solid rgba(255, 200, 0, 0.9);
        `;

        pageDiv.appendChild(highlight);
        console.log('✓ Highlight created and inserted');

    } catch (error) {
        console.error('Error creating highlight overlay:', error);
    }
}