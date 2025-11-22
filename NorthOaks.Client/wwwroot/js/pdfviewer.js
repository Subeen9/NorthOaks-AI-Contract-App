let pdfViewerWindow = null;

window.initPdfViewer = function () {
    const iframe = document.getElementById('pdfViewer');
    if (!iframe) {
        console.error('PDF viewer iframe not found');
        return false;
    }
    iframe.onload = function () {
        pdfViewerWindow = iframe.contentWindow;
        console.log('PDF viewer iframe loaded');
    };
    return true;
};

window.onPdfViewerLoaded = function () {
    const iframe = document.getElementById('pdfViewer');
    if (iframe && iframe.contentWindow) {
        pdfViewerWindow = iframe.contentWindow;
        console.log('PDF viewer window reference updated');
    }
};

window.pdfViewerClearHighlights = function () {
    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            console.error('Cannot clear highlights: iframe not accessible');
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

        console.log('All highlights cleared');
        return true;
    } catch (error) {
        console.error('Error clearing highlights:', error);
        return false;
    }
};

window.pdfViewerGoToPage = function (pageNumber) {
    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            console.error('Cannot navigate: iframe not accessible');
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const pdfViewer = iframeWindow.PDFViewerApplication?.pdfViewer;

        if (!pdfViewer) {
            console.error('PDFViewerApplication.pdfViewer not available');
            return false;
        }

        pdfViewer.currentPageNumber = pageNumber;
        console.log(`✓ Navigated to page ${pageNumber}`);
        return true;
    } catch (error) {
        console.error('Error navigating to page:', error);
        return false;
    }
};

window.pdfViewerHighlightText = function (searchText) {
    console.log(`=== Starting highlight (${searchText.length} chars) ===`);
    console.log(`Preview: "${searchText.substring(0, 80)}..."`);

    try {
        const iframe = document.getElementById('pdfViewer');
        if (!iframe || !iframe.contentWindow) {
            console.error('PDF viewer iframe not accessible');
            return false;
        }

        const iframeWindow = iframe.contentWindow;
        const iframeDoc = iframeWindow.document;
        const pdfViewer = iframeWindow.PDFViewerApplication?.pdfViewer;

        if (!pdfViewer) {
            console.error('PDFViewerApplication not available');
            return false;
        }

        const currentPageNumber = pdfViewer.currentPageNumber;
        console.log(`Current page: ${currentPageNumber}`);

        return smartParagraphHighlight(iframeDoc, currentPageNumber, searchText);

    } catch (error) {
        console.error('Error in pdfViewerHighlightText:', error);
        console.error('Stack:', error.stack);
        return false;
    }
};

/**
 * Smart highlighting that preserves paragraph structure
 * Uses fuzzy matching to handle PDF text extraction quirks
 */
function smartParagraphHighlight(iframeDoc, pageNumber, searchText) {
    try {
        const pageDiv = iframeDoc.querySelector(`.page[data-page-number="${pageNumber}"]`);
        if (!pageDiv) {
            console.error(`Page ${pageNumber} not found`);
            return false;
        }

        const textLayer = pageDiv.querySelector('.textLayer');
        if (!textLayer) {
            console.error('Text layer not found');
            return false;
        }

        const textSpans = Array.from(textLayer.querySelectorAll('span'));
        if (textSpans.length === 0) {
            console.error('No text spans found');
            return false;
        }

        console.log(`Found ${textSpans.length} text spans on page`);

        const pageTextData = buildPageTextMap(textSpans);
        const { fullText, spanMap } = pageTextData;

        console.log(`Page text length: ${fullText.length} characters`);

        let matchResult = attemptExactMatch(fullText, searchText, spanMap);

        if (!matchResult) {
            console.log('Exact match failed, trying fuzzy match...');
            matchResult = attemptFuzzyMatch(fullText, searchText, spanMap);
        }

        if (!matchResult) {
            console.log('Fuzzy match failed, trying sentence-based match...');
            matchResult = attemptSentenceMatch(fullText, searchText, spanMap);
        }

        if (!matchResult) {
            console.log(' All matching strategies failed, using intelligent keyword fallback...');
            return intelligentKeywordFallback(textSpans, searchText, pageDiv, spanMap);
        }

        console.log(`✓ Found match: ${matchResult.spansToHighlight.length} spans`);
        matchResult.spansToHighlight.forEach(span => {
            createHighlightOverlay(span, pageDiv);
        });

        return true;
    } catch (error) {
        console.error('Error in smart paragraph highlighting:', error);
        return false;
    }
}

/**
 * Build a map of text spans preserving spacing and structure
 */
function buildPageTextMap(textSpans) {
    let fullText = '';
    const spanMap = [];

    textSpans.forEach((span, index) => {
        const text = span.textContent || '';
        spanMap.push({
            span: span,
            startIndex: fullText.length,
            endIndex: fullText.length + text.length,
            originalText: text,
            index: index
        });
        fullText += text;
    });

    return { fullText, spanMap };
}


function attemptExactMatch(pageText, searchText, spanMap) {
    let index = pageText.indexOf(searchText);
    if (index !== -1) {
        console.log('Found exact character match');
        return getSpansForRange(index, index + searchText.length, spanMap);
    }

    const normalizedPageText = pageText.replace(/\s+/g, ' ');
    const normalizedSearchText = searchText.replace(/\s+/g, ' ');

    index = normalizedPageText.indexOf(normalizedSearchText);
    if (index !== -1) {
        console.log('Found match with normalized whitespace');
        return getSpansForRange(index, index + normalizedSearchText.length, spanMap);
    }

    return null;
}

/**
 * Strategy 2: Fuzzy match - find match with slight variations
 */
function attemptFuzzyMatch(pageText, searchText, spanMap) {
    const quarterLength = Math.floor(searchText.length / 4);
    const firstQuarter = searchText.substring(0, quarterLength);
    const lastQuarter = searchText.substring(searchText.length - quarterLength);

    const firstIndex = pageText.indexOf(firstQuarter);
    const lastIndex = pageText.lastIndexOf(lastQuarter);

    if (firstIndex !== -1 && lastIndex !== -1 && lastIndex >= firstIndex) {
        console.log('Found fuzzy match using first and last quarters');
        return getSpansForRange(firstIndex, lastIndex + lastQuarter.length, spanMap);
    }

    const halfLength = Math.floor(searchText.length / 2);
    const firstHalf = searchText.substring(0, halfLength);
    const firstHalfIndex = pageText.indexOf(firstHalf);

    if (firstHalfIndex !== -1) {
        console.log('Found fuzzy match using first half');
       const remainingText = searchText.substring(halfLength);
        const keywords = remainingText.split(/\s+/).slice(0, 5).filter(w => w.length > 3);

        let endIndex = firstHalfIndex + firstHalf.length;
        for (const keyword of keywords) {
            const kwIndex = pageText.indexOf(keyword, firstHalfIndex);
            if (kwIndex !== -1 && kwIndex < firstHalfIndex + searchText.length + 200) {
                endIndex = kwIndex + keyword.length;
            }
        }

        return getSpansForRange(firstHalfIndex, endIndex, spanMap);
    }

    return null;
}


function attemptSentenceMatch(pageText, searchText, spanMap) {
    const firstSentence = extractFirstSentence(searchText);

    if (firstSentence.length > 30) {  
        const index = pageText.indexOf(firstSentence);
        if (index !== -1) {
            console.log(' Found match using first sentence');
            const endIndex = Math.min(index + searchText.length, pageText.length);
            return getSpansForRange(index, endIndex, spanMap);
        }
    }

    
    const numberedSectionMatch = searchText.match(/^(\d+\.\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)/);
    if (numberedSectionMatch) {
        const sectionTitle = numberedSectionMatch[1];
        if (sectionTitle.length >= 10) { 
            const index = pageText.indexOf(sectionTitle);
            if (index !== -1) {
                console.log(`✓ Found match using numbered section: "${sectionTitle}"`);
                const endIndex = Math.min(index + searchText.length, pageText.length);
                return getSpansForRange(index, endIndex, spanMap);
            }
        }
    }

    return null;
}

/**
 * Intelligent keyword fallback - highlights contextual phrases not random keywords
 */
function intelligentKeywordFallback(textSpans, searchText, pageDiv, spanMap) {
    const stopWords = new Set([
        'the', 'a', 'an', 'and', 'or', 'but', 'in', 'on', 'at', 'to', 'for', 'of', 'with',
        'is', 'was', 'are', 'were', 'be', 'been', 'this', 'that', 'these', 'those',
        'from', 'by', 'as', 'into', 'through', 'during', 'before', 'after', 'above', 'below',
        'then', 'when', 'where', 'who', 'what', 'which', 'how', 'his', 'her', 'their', 'have', 'has'
    ]);

     
    const keywords = searchText
        .split(/[\s\n]+/)
        .filter(w => {
            const clean = w.toLowerCase().replace(/[^\w]/g, '');
            return clean.length > 4 && !stopWords.has(clean);
        })
        .slice(0, 20)
        .map(w => w.toLowerCase().replace(/[^\w]/g, ''));

    if (keywords.length === 0) {
        console.warn('Could not extract meaningful keywords');
        return false;
    }

    console.log(`Using ${keywords.length} key terms: ${keywords.slice(0, 5).join(', ')}...`);

    const visibleSpans = textSpans.filter(span => {
        const rect = span.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    });

    console.log(`Filtered to ${visibleSpans.length} visible spans (from ${textSpans.length} total)`);

    const highlightedSpans = new Set();
    let currentGroup = [];
    let groupKeywordCount = 0;

    visibleSpans.forEach((span, idx) => {
        const spanText = (span.textContent || '').toLowerCase().replace(/[^\w\s]/g, '');
        const matchedKeywords = keywords.filter(kw => spanText.includes(kw));

        if (matchedKeywords.length >= 1) {
            currentGroup.push(span);
            groupKeywordCount += matchedKeywords.length;
        } else {
            
            if (groupKeywordCount >= 3 && currentGroup.length >= 2) {
                currentGroup.forEach(s => highlightedSpans.add(s));
            }
            currentGroup = [];
            groupKeywordCount = 0;
        }
    });

    if (groupKeywordCount >= 3 && currentGroup.length >= 2) {
        currentGroup.forEach(s => highlightedSpans.add(s));
    }

    if (highlightedSpans.size === 0) {
        console.warn('Fallback could not find matching text');
        return false;
    }

    console.log(`Fallback highlighted ${highlightedSpans.size} spans`);
    highlightedSpans.forEach(span => {
        createHighlightOverlay(span, pageDiv);
    });

    return true;
}

function extractFirstSentence(text) {
    const match = text.match(/^([^.!?]{20,}?[.!?])/);
    return match ? match[1].trim() : text.substring(0, 100);
}


function extractTitleTerms(text) {
    const terms = text
        .split(/[\s\n]+/)
        .filter(w => w.length > 3 && /^[A-Z]/.test(w))
        .slice(0, 5)
        .map(w => w.replace(/[^\w]/g, ''));

    return terms.filter(t => t.length > 0);
}

function getSpansForRange(startIndex, endIndex, spanMap) {
    const overlappingSpans = spanMap.filter(sm =>
        sm.endIndex > startIndex && sm.startIndex < endIndex
    );

    if (overlappingSpans.length === 0) {
        return null;
    }

    
    const actualSpans = overlappingSpans.map(sm => sm.span).filter(span => {
        const rect = span.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    });

    if (actualSpans.length === 0) {
        console.warn('All matched spans have zero dimensions, trying to expand range');

        const expandedSpans = spanMap.filter(sm =>
            sm.endIndex > startIndex - 100 && sm.startIndex < endIndex + 100
        ).map(sm => sm.span).filter(span => {
            const rect = span.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
        });

        if (expandedSpans.length > 0) {
            console.log(`Expanded range found ${expandedSpans.length} visible spans`);
            return {
                spansToHighlight: expandedSpans,
                startIndex,
                endIndex,
                spanCount: expandedSpans.length
            };
        }

        return null;
    }

    return {
        spansToHighlight: actualSpans,
        startIndex,
        endIndex,
        spanCount: actualSpans.length
    };
}

/**
 * Create visual highlight overlay with better positioning
 */
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