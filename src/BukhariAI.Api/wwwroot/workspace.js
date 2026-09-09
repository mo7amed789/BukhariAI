/**
 * ==========================================================================
 * BukhariAI — Learner Workspace Client Application
 * Intelligent Tutor & Adaptive Progressive Learning System
 * ==========================================================================
 */

(function () {
  'use strict';

  // Global fetch interceptor to automatically bypass ngrok browser warning on all requests
  const _nativeFetch = window.fetch;
  if (_nativeFetch) {
    window.fetch = function (resource, init) {
      init = init || {};
      const headers = new Headers(init.headers || {});
      if (!headers.has('ngrok-skip-browser-warning')) {
        headers.set('ngrok-skip-browser-warning', 'true');
      }
      return _nativeFetch.call(this, resource, {
        ...init,
        headers
      });
    };
  }

  // Application State
  const state = {
    // User Authentication & Identity State
    currentUser: null,
    authToken: localStorage.getItem('bukhariai_token') || null,

    books: [],
    activeBook: null,
    learningContext: null,
    dashboard: null,
    nextAction: null,
    dueReviews: [],
    weakConcepts: [],
    lessons: [],
    bookLessons: new Map(),

    // Current Lesson & Reading state
    currentLesson: null,
    lessonProgress: null,

    // Active Assessment Session
    activeAssessment: null,
    currentQuestionIndex: 0,
    assessmentResults: [],

    // UI state
    activeView: 'dashboard',
    toastTimer: null,

    // Ingestion source state
    sourceMode: 'pdf',
    pastedImages: [],
    bookPdfs: new Map(), // Map<string, File>
    currentPdfFile: null, // Preserved active File object

    // Reviews & Consolidation state
    activeReviewTab: 'plan',
    flashcards: [],
    currentFlashcardIndex: 0,
    activeReinforceSession: null,
    masteryMatrix: [],
    matrixFilter: 'all',
    matrixSearchQuery: '',
    quickQuiz: null,
    quickQuizAnswers: new Map(),

    // Lesson Chatbot state
    chatMessages: [],
    isChatOpen: false,
    isChatLoading: false,
    chatContextLessonId: null,
    chatSessionId: null,
    chatSessions: []
  };

  // ═══════════════════════════════════════════════════════════
  // PERSISTENT PDF STORAGE & BOOK-PDF RETENTION LAYER (IndexedDB)
  // ═══════════════════════════════════════════════════════════
  const PDF_DB_NAME = 'dirayah_pdf_storage';
  const PDF_DB_VERSION = 1;
  const PDF_STORE_NAME = 'books';
  let _pdfDbPromise = null;

  function getPdfDb() {
    if (_pdfDbPromise) return _pdfDbPromise;
    _pdfDbPromise = new Promise((resolve) => {
      if (typeof window === 'undefined' || !window.indexedDB) {
        resolve(null);
        return;
      }
      try {
        const req = window.indexedDB.open(PDF_DB_NAME, PDF_DB_VERSION);
        req.onupgradeneeded = (e) => {
          const db = e.target.result;
          if (!db.objectStoreNames.contains(PDF_STORE_NAME)) {
            db.createObjectStore(PDF_STORE_NAME, { keyPath: 'bookId' });
          }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = (err) => {
          console.warn('IndexedDB failed to open:', err);
          resolve(null);
        };
      } catch (err) {
        console.warn('IndexedDB exception:', err);
        resolve(null);
      }
    });
    return _pdfDbPromise;
  }

  async function savePdfToStorage(bookId, file) {
    if (!file) return;
    try {
      const db = await getPdfDb();
      if (!db) return;
      const tx = db.transaction(PDF_STORE_NAME, 'readwrite');
      const store = tx.objectStore(PDF_STORE_NAME);
      store.put({
        bookId: String(bookId || '__default__'),
        name: file.name,
        size: file.size,
        type: file.type || 'application/pdf',
        lastModified: file.lastModified,
        file: file,
        savedAt: Date.now()
      });
      return new Promise((resolve) => {
        tx.oncomplete = () => resolve(true);
        tx.onerror = (err) => {
          console.warn('IndexedDB save failed:', err);
          resolve(false);
        };
      });
    } catch (err) {
      console.warn('savePdfToStorage error:', err);
    }
  }

  async function loadPdfFromStorage(bookId) {
    try {
      const db = await getPdfDb();
      if (!db) return null;
      const tx = db.transaction(PDF_STORE_NAME, 'readonly');
      const store = tx.objectStore(PDF_STORE_NAME);
      const req = store.get(String(bookId || '__default__'));
      return new Promise((resolve) => {
        req.onsuccess = () => {
          const record = req.result;
          if (record && record.file) {
            let file = record.file;
            if (!(file instanceof File)) {
              file = new File([record.file], record.name || 'book.pdf', {
                type: record.type || 'application/pdf',
                lastModified: record.lastModified || Date.now()
              });
            }
            resolve(file);
          } else {
            resolve(null);
          }
        };
        req.onerror = () => resolve(null);
      });
    } catch (err) {
      console.warn('loadPdfFromStorage error:', err);
      return null;
    }
  }

  async function deletePdfFromStorage(bookId) {
    try {
      const db = await getPdfDb();
      if (!db) return;
      const tx = db.transaction(PDF_STORE_NAME, 'readwrite');
      const store = tx.objectStore(PDF_STORE_NAME);
      store.delete(String(bookId || '__default__'));
    } catch (err) {
      console.warn('deletePdfFromStorage error:', err);
    }
  }

  function formatFileSize(bytes) {
    if (!bytes || bytes <= 0) return '';
    if (bytes < 1024 * 1024) {
      const kb = (bytes / 1024).toFixed(1);
      return `${kb} ك.ب`;
    }
    const mb = (bytes / (1024 * 1024)).toFixed(1);
    return `${mb} م.ب`;
  }

  function updatePdfDropZoneUI(file) {
    const dropZone = $('#pdf-drop-zone');
    const chosenText = $('#file-chosen-text');
    const chosenSubtext = $('#file-chosen-subtext');
    const actionsRow = $('#pdf-file-actions');
    const hintEl = $('#pdf-retained-hint');
    const dropIcon = $('#pdf-drop-icon');

    if (file) {
      if (dropZone) dropZone.classList.add('has-file');
      if (dropIcon) dropIcon.textContent = '📖';
      if (chosenText) chosenText.textContent = file.name;
      if (chosenSubtext) {
        chosenSubtext.textContent = `الحجم: ${formatFileSize(file.size)} • ملف الكتاب معتمد ومحفوظ في المنصة ✓`;
        chosenSubtext.hidden = false;
      }
      if (actionsRow) actionsRow.hidden = false;
      if (hintEl) hintEl.hidden = false;
    } else {
      if (dropZone) dropZone.classList.remove('has-file');
      if (dropIcon) dropIcon.textContent = '📁';
      if (chosenText) chosenText.textContent = 'انقر لاختيار ملف PDF أو اسحبه إلى هنا';
      if (chosenSubtext) {
        chosenSubtext.textContent = '';
        chosenSubtext.hidden = true;
      }
      if (actionsRow) actionsRow.hidden = true;
      if (hintEl) hintEl.hidden = true;
    }
  }

  async function setPreservedPdf(file, bookId) {
    if (!file) return;
    state.currentPdfFile = file;

    const normalizedBookId = (bookId && bookId !== '__create_new__') ? bookId : (state.activeBook?.id || '__default__');
    state.bookPdfs.set(normalizedBookId, file);
    state.bookPdfs.set('__last_active__', file);

    // Persist to IndexedDB
    await savePdfToStorage(normalizedBookId, file);
    await savePdfToStorage('__last_active__', file);

    updatePdfDropZoneUI(file);

    // Also populate HTMLInputElement.files if DataTransfer is supported
    try {
      const fileInput = $('#pdf-file-input');
      if (fileInput) {
        const dt = new DataTransfer();
        dt.items.add(file);
        fileInput.files = dt.files;
      }
    } catch (_) {}
  }

  async function switchBookPdf(bookId) {
    if (!bookId || bookId === '__create_new__') {
      if (state.currentPdfFile) {
        updatePdfDropZoneUI(state.currentPdfFile);
      }
      return;
    }

    // 1. Check in-memory map
    let file = state.bookPdfs.get(bookId);

    // 2. If not in memory, check IndexedDB for this book
    if (!file) {
      file = await loadPdfFromStorage(bookId);
      if (file) {
        state.bookPdfs.set(bookId, file);
      }
    }

    // 3. Fallback: last active PDF
    if (!file) {
      const last = state.bookPdfs.get('__last_active__') || await loadPdfFromStorage('__last_active__');
      if (last) {
        file = last;
        state.bookPdfs.set(bookId, file);
        await savePdfToStorage(bookId, file);
      }
    }

    // 4. Fallback: keep current in-memory PDF if available
    if (!file && state.currentPdfFile) {
      file = state.currentPdfFile;
      if (bookId && bookId !== '__create_new__') {
        state.bookPdfs.set(bookId, file);
        await savePdfToStorage(bookId, file);
      }
    }

    if (file) {
      state.currentPdfFile = file;
      updatePdfDropZoneUI(file);
      try {
        const fileInput = $('#pdf-file-input');
        if (fileInput) {
          const dt = new DataTransfer();
          dt.items.add(file);
          fileInput.files = dt.files;
        }
      } catch (_) {}
    } else if (!state.currentPdfFile) {
      updatePdfDropZoneUI(null);
      const fileInput = $('#pdf-file-input');
      if (fileInput) fileInput.value = '';
    }

    suggestNextPageRangeForBook(bookId);
  }

  async function clearPreservedPdf(bookId) {
    const targetId = bookId || $('#upload-target-book-select')?.value || state.activeBook?.id;
    state.currentPdfFile = null;
    if (targetId && targetId !== '__create_new__') {
      state.bookPdfs.delete(targetId);
      await deletePdfFromStorage(targetId);
    }
    state.bookPdfs.delete('__last_active__');
    await deletePdfFromStorage('__last_active__');

    const fileInput = $('#pdf-file-input');
    if (fileInput) fileInput.value = '';
    updatePdfDropZoneUI(null);
    showToast('تمت إزالة ملف PDF المعتمد لهذا الكتاب.', 'info');
  }

  function suggestNextPageRangeForBook(bookId) {
    if (!bookId || bookId === '__create_new__') return;
    const book = state.books.find(b => b.id === bookId);
    let maxEnd = 0;
    if (book && book.lessons && book.lessons.length > 0) {
      book.lessons.forEach(l => {
        if (l.endPage && l.endPage > maxEnd) maxEnd = l.endPage;
        else if (l.startPage && l.startPage > maxEnd) maxEnd = l.startPage;
      });
    } else if (state.activeBook && state.activeBook.id === bookId && state.lessons && state.lessons.length > 0) {
      state.lessons.forEach(l => {
        if (l.endPage && l.endPage > maxEnd) maxEnd = l.endPage;
        else if (l.startPage && l.startPage > maxEnd) maxEnd = l.startPage;
      });
    }

    const startInput = $('#start-page-input');
    const endInput = $('#end-page-input');
    if (startInput && endInput && maxEnd > 0) {
      const currentStart = parseInt(startInput.value, 10);
      // Only suggest if not already set or not already advanced beyond maxEnd
      if (isNaN(currentStart) || currentStart <= maxEnd) {
        startInput.value = maxEnd + 1;
        endInput.value = maxEnd + 2;
        endInput.min = maxEnd + 1;
        endInput.max = maxEnd + 2;
      }
    }
  }

  async function initPdfStorage() {
    try {
      const targetId = localStorage.getItem('dirayah_default_book_id') || localStorage.getItem('dirayah_selected_book_id');
      if (targetId) {
        const file = await loadPdfFromStorage(targetId) || await loadPdfFromStorage('__last_active__');
        if (file) {
          state.bookPdfs.set(targetId, file);
          state.currentPdfFile = file;
          updatePdfDropZoneUI(file);
        }
      }
    } catch (err) {
      console.warn('initPdfStorage error:', err);
    }
  }

  // Scratch Learning & Roadmap State
  const scratchState = {
    curatedTracks: [],
    currentRoadmap: null,
    activeLevelNumber: 1,
    activeMilestoneId: null,
    completedMilestones: new Set(),
    isLoading: false,
    isTutorLoading: false,
    lastQuery: ''
  };

  // Helper Selectors
  const $ = (selector) => document.querySelector(selector);
  const $$ = (selector) => document.querySelectorAll(selector);

  // Friendly Pedagogical Action Mappings
  const ACTION_DEFINITIONS = {
    // String names
    'StartNewLesson': {
      type: 'درس جديد',
      title: 'ابدأ درسًا جديدًا',
      btnText: 'بدء الدرس الجديد',
      defaultReason: 'سنبدأ معًا دراسة مقطع جديد من الكتاب وتأسيس مفاهيمه وأدلته الفقهية والعلمية.'
    },
    'ContinueLesson': {
      type: 'متابعة الدرس',
      title: 'نكمل شرح هذا الدرس',
      btnText: 'متابعة الدرس',
      defaultReason: 'نكمل قراءة هذا الدرس واستيعاب المسائل العلمية والأدلة المرتبطة به.'
    },
    'ReviewConcept': {
      type: 'مراجعة دورية',
      title: 'راجع ما تعلمته',
      btnText: 'بدء المراجعة',
      defaultReason: 'هذا المفهوم يحتاج إلى مراجعة قصيرة قبل أن نكمل لترسيخ حفظه واستدلاله.'
    },
    'ReinforceConcept': {
      type: 'تثبيت الفهم',
      title: 'نثبت هذا المفهوم',
      btnText: 'تثبيت المفهوم',
      defaultReason: 'سنشرح هذا المفهوم من زاوية أوضح مع بسط أدلته لنثبت استيعابك له.'
    },
    'TakeAssessment': {
      type: 'تحقق من الفهم',
      title: 'اختبر فهمك',
      btnText: 'الانتقال للاختبار',
      defaultReason: 'حان وقت التحقق من استيعابك للمسألة والأدلة من خلال أسئلة تفاعلية.'
    },
    'VerifyMastery': {
      type: 'إتقان المادة',
      title: 'تحقق من إتقانك',
      btnText: 'التحقق من الإتقان',
      defaultReason: 'التحقق النهائي من إتقانك للمفاهيم السابقة والقدرة على ربطها وتطبيقها.'
    },
    'ContinueToNextPages': {
      type: 'نهاية المقطع',
      title: 'وصلنا إلى آخر الصفحات المتاحة',
      btnText: 'إضافة صفحات جديدة',
      defaultReason: 'أتممت دراسة الصفحات المحفوظة حاليًا بنجاح. أضف المقطع التالي لمواصلة التعلّم.'
    },
    'Completed': {
      type: 'إتمام المسار',
      title: 'أكملت المحتوى الحالي',
      btnText: 'إضافة صفحات جديدة',
      defaultReason: 'أحسنت! أتممت جميع الدروس والتقييمات في هذا النطاق بنجاح.'
    },

    // Numeric enum fallback
    0: { type: 'درس جديد', title: 'ابدأ درسًا جديدًا', btnText: 'بدء الدرس', defaultReason: 'سنبدأ معًا دراسة مقطع جديد من الكتاب.' },
    1: { type: 'متابعة الدرس', title: 'نكمل شرح هذا الدرس', btnText: 'متابعة الدرس', defaultReason: 'نكمل قراءة هذا المقطع واستيعاب مسائله العلمية.' },
    2: { type: 'مراجعة دورية', title: 'راجع ما تعلمته', btnText: 'بدء المراجعة', defaultReason: 'هذا المفهوم يحتاج إلى مراجعة قصيرة قبل أن نكمل.' },
    3: { type: 'تثبيت الفهم', title: 'نثبت هذا المفهوم', btnText: 'تثبيت المفهوم', defaultReason: 'سنشرح هذا المفهوم من زاوية أوضح لتثبيت فهمه.' },
    LearnNewLesson: {
      typeText: 'مدارسة مقطع جديد',
      icon: '📖',
      description: 'أنت جاهز الآن لمدارسة واستيعاب مسائل جديدة من الكتاب بتوجيه المعلم وتحليل الأدلة.'
    },
    ReviewWeakConcept: {
      typeText: 'تثبيت مسألة تحتاج تركيزاً',
      icon: '🎯',
      description: 'أظهر التقييم مسألة تحتاج إلى إعادة مراجعة وضبط لاستيعابها وإتقانها التام.'
    },
    ReviewRetentiveSpacing: {
      typeText: 'مراجعة تثبيت دورية',
      icon: '🔄',
      description: 'حان موعد مراجعة مسائل سابقة لترسيخها في الذاكرة التراكمية ومنع نسيانها.'
    },
    TakePendingAssessment: {
      typeText: 'تقييم الاستيعاب',
      icon: '✍️',
      description: 'أتممت قراءة المقطع، والآن وقت تقييم فهمك واستنباطك الفقهي بإشراف المعلم.'
    },
    ContinueReading: {
      typeText: 'متابعة القراءة والتحليل',
      icon: '🔍',
      description: 'أنت في منتصف مدارسة هذا الدرس؛ تابع تحليل المتن والأدلة لإتمامه.'
    }
  };

  // Safe HTML Escaping
  function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  // Format Arabic Numbers
  function toArabicDigits(num) {
    if (num == null) return '٠';
    const arabicDigits = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];
    return String(num).replace(/[0-9]/g, (d) => arabicDigits[d]);
  }

  // Toast Notification System
  function showToast(message, type = 'info', title = null) {
    const toast = $('#notice-toast');
    const toastTitle = $('#toast-title');
    const toastMsg = $('#toast-message');
    const toastIcon = $('#toast-icon');

    if (!toast) return;

    if (state.toastTimer) {
      clearTimeout(state.toastTimer);
    }

    toast.className = `notice-toast toast-${type}`;
    toastTitle.textContent = title || (type === 'error' ? 'تنبيه خطأ' : type === 'warning' ? 'ملاحظة' : type === 'success' ? 'تمت العملية' : 'تنبيه');
    toastMsg.textContent = message;

    const icons = {
      info: 'ℹ️',
      success: '✓',
      warning: '⚠️',
      error: '❌'
    };
    toastIcon.textContent = icons[type] || 'ℹ️';

    toast.hidden = false;

    state.toastTimer = setTimeout(() => {
      toast.hidden = true;
    }, 6500);
  }

  // Safe API Client with Structured Error Handling
  async function api(endpoint, options = {}) {
    const defaultHeaders = {
      'Accept': 'application/json',
      'ngrok-skip-browser-warning': 'true'
    };

    const token = state.authToken || localStorage.getItem('bukhariai_token');
    if (token) {
      defaultHeaders['Authorization'] = `Bearer ${token}`;
    }

    if (!(options.body instanceof FormData)) {
      defaultHeaders['Content-Type'] = 'application/json';
    }

    try {
      const response = await fetch(`/api${endpoint}`, {
        ...options,
        headers: {
          ...defaultHeaders,
          ...options.headers
        }
      });

      if (response.status === 204) return null;

      if (!response.ok) {
        if (response.status === 429) {
          throw new Error('الخدمة مشغولة حاليًا بسبب قيود سعة المعالجة المؤقتة. يُرجى الانتظار دقيقة واحدة ثم إعادة المحاولة.');
        }
        let errorPayload = null;
        try {
          errorPayload = await response.json();
        } catch {
          // ignore non-json error responses
        }
        const message = errorPayload?.error || errorPayload?.detail || errorPayload?.title || `خطأ في الخادم (${response.status})`;
        throw new Error(message);
      }

      return await response.json();
    } catch (err) {
      if (err.name === 'TypeError' && err.message.includes('fetch')) {
        throw new Error('تعذر الاتصال بالخادم. يرجى التحقق من اتصالك بالشبكة ثم إعادة المحاولة.');
      }
      throw err;
    }
  }

  // Helper alias for api() with or without '/api' prefix
  async function apiFetch(endpoint, options = {}) {
    const cleanEndpoint = endpoint.startsWith('/api') ? endpoint.substring(4) : endpoint;
    return await api(cleanEndpoint, options);
  }

  // View Navigation Manager
  function switchView(viewName) {
    state.activeView = viewName;

    // Update Nav buttons
    let activeBtn = null;
    $$('.nav-btn').forEach((btn) => {
      const isActive = btn.dataset.view === viewName;
      btn.classList.toggle('active', isActive);
      if (isActive) activeBtn = btn;
    });

    if (activeBtn && typeof activeBtn.scrollIntoView === 'function') {
      activeBtn.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
    }

    // Update view sections
    const views = {
      'dashboard': $('#view-dashboard'),
      'scratch': $('#view-scratch'),
      'lessons': $('#view-lessons'),
      'reviews': $('#view-reviews'),
      'reader': $('#view-reader'),
      'assessment': $('#view-assessment'),
      'upload': $('#view-upload'),
      'settings': $('#view-settings'),
      'quran': $('#view-quran')
    };

    Object.entries(views).forEach(([name, el]) => {
      if (el) {
        const isActive = name === viewName;
        el.hidden = !isActive;
        el.classList.toggle('active', isActive);
      }
    });

    if (viewName === 'upload') {
      syncUploadBookSelector();
      const currentBookId = $('#upload-target-book-select')?.value || state.activeBook?.id;
      switchBookPdf(currentBookId);
    }

    if (viewName !== 'reader' && typeof BukhariTextReader !== 'undefined' && BukhariTextReader.isPlaying) {
      BukhariTextReader.stop();
    }

    if (typeof updateChatContextDisplay === 'function') {
      updateChatContextDisplay();
    }

    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // ==========================================================================
  // User Authentication, Profile & Identity Manager
  // ==========================================================================

  async function initAuth() {
    setupAuthUIListeners();
    const token = localStorage.getItem('bukhariai_token');
    if (token) {
      state.authToken = token;
      try {
        const user = await api('/auth/me');
        state.currentUser = user;
      } catch (err) {
        console.warn('Stored auth token expired or invalid:', err);
        localStorage.removeItem('bukhariai_token');
        state.authToken = null;
        state.currentUser = null;
      }
    }
    renderUserAuthUI();
  }

  function renderUserAuthUI() {
    const guestButtons = $('#auth-guest-buttons');
    const userWrapper = $('#user-auth-wrapper');
    const displayNameEl = $('#user-display-name');
    const dropdownUsernameEl = $('#dropdown-username');
    const dropdownEmailEl = $('#dropdown-email');
    const roleBadgeEl = $('#user-role-badge');

    if (state.currentUser) {
      const roleMap = {
        0: 'طالب علم',
        1: 'باحث / مراجع',
        2: 'مشرف النظام',
        'Student': 'طالب علم',
        'Researcher': 'باحث / مراجع',
        'Admin': 'مشرف النظام'
      };
      if (guestButtons) {
        guestButtons.hidden = true;
        guestButtons.setAttribute('hidden', '');
        guestButtons.style.display = 'none';
      }
      if (userWrapper) {
        userWrapper.hidden = false;
        userWrapper.removeAttribute('hidden');
        userWrapper.style.display = 'flex';
      }
      if (displayNameEl) displayNameEl.textContent = state.currentUser.username;
      if (dropdownUsernameEl) dropdownUsernameEl.textContent = state.currentUser.username;
      if (dropdownEmailEl) dropdownEmailEl.textContent = state.currentUser.email || '';
      if (roleBadgeEl) roleBadgeEl.textContent = roleMap[state.currentUser.role] || 'مستخدم';
    } else {
      if (guestButtons) {
        guestButtons.hidden = false;
        guestButtons.removeAttribute('hidden');
        guestButtons.style.display = 'flex';
      }
      if (userWrapper) {
        userWrapper.hidden = true;
        userWrapper.setAttribute('hidden', '');
        userWrapper.style.display = 'none';
      }
    }
  }

  function openAuthModal(defaultTab = 'login') {
    const authModal = $('#auth-modal');
    if (!authModal) {
      console.error('Modal #auth-modal not found in DOM.');
      return;
    }
    authModal.hidden = false;
    authModal.removeAttribute('hidden');
    authModal.style.display = 'flex';
    switchAuthTab(defaultTab);
  }

  function closeAuthModal() {
    const authModal = $('#auth-modal');
    if (!authModal) return;
    authModal.hidden = true;
    authModal.setAttribute('hidden', '');
    authModal.style.display = 'none';
  }

  function switchAuthTab(tab) {
    $$('.auth-tab-btn').forEach((btn) => {
      const isActive = btn.dataset.tab === tab;
      btn.classList.toggle('active', isActive);
      btn.setAttribute('aria-selected', String(isActive));
    });

    const loginPane = $('#auth-form-login');
    const regPane = $('#auth-form-register');
    const forgotPane = $('#auth-tab-forgot-pane');
    const modalTitle = $('#auth-modal-title');

    if (loginPane) {
      const isVisible = tab === 'login';
      loginPane.hidden = !isVisible;
      if (isVisible) {
        loginPane.removeAttribute('hidden');
        loginPane.style.display = 'flex';
      } else {
        loginPane.setAttribute('hidden', '');
        loginPane.style.display = 'none';
      }
    }
    if (regPane) {
      const isVisible = tab === 'register';
      regPane.hidden = !isVisible;
      if (isVisible) {
        regPane.removeAttribute('hidden');
        regPane.style.display = 'flex';
      } else {
        regPane.setAttribute('hidden', '');
        regPane.style.display = 'none';
      }
    }
    if (forgotPane) {
      const isVisible = tab === 'forgot';
      forgotPane.hidden = !isVisible;
      if (isVisible) {
        forgotPane.removeAttribute('hidden');
        forgotPane.style.display = 'block';
      } else {
        forgotPane.setAttribute('hidden', '');
        forgotPane.style.display = 'none';
      }
    }

    if (modalTitle) {
      if (tab === 'login') modalTitle.textContent = 'تسجيل الدخول إلى دِراية AI';
      else if (tab === 'register') modalTitle.textContent = 'إنشاء حساب جديد في دِراية AI';
      else if (tab === 'forgot') modalTitle.textContent = 'استعادة كلمة المرور';
    }

    $$('.auth-error-msg').forEach((msg) => { msg.hidden = true; msg.textContent = ''; });
  }

  function setupAuthUIListeners() {
    const profileBtn = $('#user-profile-btn');
    const dropdownMenu = $('#user-dropdown-menu');
    const modalCloseBtn = $('#auth-modal-close');
    const authModal = $('#auth-modal');

    // Direct header login and register buttons
    $('#header-login-btn')?.addEventListener('click', (e) => {
      e.preventDefault();
      openAuthModal('login');
    });

    $('#header-register-btn')?.addEventListener('click', (e) => {
      e.preventDefault();
      openAuthModal('register');
    });

    // Toggle user dropdown menu (when authenticated)
    profileBtn?.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!dropdownMenu) return;
      const isHidden = dropdownMenu.hidden;
      dropdownMenu.hidden = !isHidden;
      profileBtn.setAttribute('aria-expanded', String(!isHidden));
    });

    // Close dropdown on click outside
    document.addEventListener('click', (e) => {
      if (dropdownMenu && !dropdownMenu.hidden && !e.target.closest('#user-auth-wrapper')) {
        dropdownMenu.hidden = true;
        profileBtn?.setAttribute('aria-expanded', 'false');
      }
    });

    // Modal Close
    modalCloseBtn?.addEventListener('click', closeAuthModal);
    authModal?.addEventListener('click', (e) => {
      if (e.target === authModal) {
        closeAuthModal();
      }
    });

    // Logout
    $('#logout-btn')?.addEventListener('click', async () => {
      if (dropdownMenu) dropdownMenu.hidden = true;
      localStorage.removeItem('bukhariai_token');
      state.authToken = null;
      state.currentUser = null;
      renderUserAuthUI();
      showToast('تم تسجيل الخروج بنجاح.', 'info');
      await loadBooks();
    });

    // Tabs switching
    $$('.auth-tab-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        switchAuthTab(tab);
      });
    });

    // "Forgot password?" link from login form
    $('#goto-forgot-link')?.addEventListener('click', () => {
      switchAuthTab('forgot');
    });

    // Login Form Submit
    let isLoggingIn = false;
    const executeLogin = async (e) => {
      if (e) e.preventDefault();
      if (isLoggingIn) return;

      const usernameInput = $('#login-username');
      const passwordInput = $('#login-password');
      const errorMsg = $('#login-error-msg');
      const submitBtn = $('#login-submit-btn');

      const username = usernameInput?.value.trim();
      const password = passwordInput?.value;

      if (!username || !password) {
        if (errorMsg) {
          errorMsg.textContent = 'يرجى إدخال اسم المستخدم وكلمة المرور.';
          errorMsg.hidden = false;
        }
        return;
      }

      try {
        isLoggingIn = true;
        if (errorMsg) errorMsg.hidden = true;
        if (submitBtn) {
          submitBtn.disabled = true;
          submitBtn.innerHTML = '<span>جاري تسجيل الدخول...</span>';
        }

        const res = await api('/auth/login', {
          method: 'POST',
          body: JSON.stringify({ usernameOrEmail: username, password })
        });

        if (res && res.token) {
          localStorage.setItem('bukhariai_token', res.token);
          state.authToken = res.token;
          state.currentUser = res.user;
          renderUserAuthUI();
          closeAuthModal();
          showToast(`مرحبًا بك مجددًا يا ${res.user.username}!`, 'success');
          await loadBooks();
        }
      } catch (err) {
        if (errorMsg) {
          errorMsg.textContent = err.message || 'فشل تسجيل الدخول. تحقق من صحة البيانات.';
          errorMsg.hidden = false;
        }
      } finally {
        isLoggingIn = false;
        if (submitBtn) {
          submitBtn.disabled = false;
          submitBtn.innerHTML = '<span>دخول</span>';
        }
      }
    };

    $('#auth-form-login')?.addEventListener('submit', executeLogin);
    $('#login-submit-btn')?.addEventListener('click', (e) => {
      executeLogin(e);
    });

    // Register Form Submit
    let isRegistering = false;
    const executeRegister = async (e) => {
      if (e) e.preventDefault();
      if (isRegistering) return;

      const usernameInput = $('#reg-username');
      const emailInput = $('#reg-email');
      const passwordInput = $('#reg-password');
      const errorMsg = $('#reg-error-msg');
      const submitBtn = $('#reg-submit-btn');

      const username = usernameInput?.value.trim();
      const email = emailInput?.value.trim();
      const password = passwordInput?.value;

      if (!username || username.length < 3) {
        if (errorMsg) {
          errorMsg.textContent = 'اسم المستخدم يجب ألا يقل عن 3 أحرف.';
          errorMsg.hidden = false;
        }
        return;
      }

      if (!email || !email.includes('@')) {
        if (errorMsg) {
          errorMsg.textContent = 'يرجى إدخال بريد إلكتروني صالح.';
          errorMsg.hidden = false;
        }
        return;
      }

      if (!password || password.length < 6) {
        if (errorMsg) {
          errorMsg.textContent = 'كلمة المرور يجب ألا تقل عن 6 خانات.';
          errorMsg.hidden = false;
        }
        return;
      }

      try {
        isRegistering = true;
        if (errorMsg) errorMsg.hidden = true;
        if (submitBtn) {
          submitBtn.disabled = true;
          submitBtn.innerHTML = '<span>جاري إنشاء الحساب...</span>';
        }

        const res = await api('/auth/register', {
          method: 'POST',
          body: JSON.stringify({ username, email, password })
        });

        if (res && res.token) {
          localStorage.setItem('bukhariai_token', res.token);
          state.authToken = res.token;
          state.currentUser = res.user;
          renderUserAuthUI();
          closeAuthModal();
          showToast(`تم إنشاء حسابك بنجاح! أهلاً بك يا ${res.user.username}.`, 'success');
          await loadBooks();
        }
      } catch (err) {
        if (errorMsg) {
          errorMsg.textContent = err.message || 'فشل إنشاء الحساب.';
          errorMsg.hidden = false;
        }
      } finally {
        isRegistering = false;
        if (submitBtn) {
          submitBtn.disabled = false;
          submitBtn.innerHTML = '<span>إنشاء الحساب والبدء</span>';
        }
      }
    };

    $('#auth-form-register')?.addEventListener('submit', executeRegister);
    $('#reg-submit-btn')?.addEventListener('click', (e) => {
      executeRegister(e);
    });

    // Forgot Password (Step 1: Request Code)
    let isRequestingCode = false;
    const executeForgot = async (e) => {
      if (e) e.preventDefault();
      if (isRequestingCode) return;

      const usernameInput = $('#forgot-username');
      const errorMsg = $('#forgot-error-msg');
      const submitBtn = $('#forgot-submit-btn');
      const identifier = usernameInput?.value.trim();

      if (!identifier) {
        if (errorMsg) {
          errorMsg.textContent = 'يرجى إدخال اسم المستخدم أو البريد الإلكتروني.';
          errorMsg.hidden = false;
        }
        return;
      }

      try {
        isRequestingCode = true;
        if (errorMsg) errorMsg.hidden = true;
        if (submitBtn) {
          submitBtn.disabled = true;
          submitBtn.innerHTML = '<span>جاري إرسال الطلب...</span>';
        }

        const res = await api('/auth/forgot-password', {
          method: 'POST',
          body: JSON.stringify({ usernameOrEmail: identifier })
        });

        const forgotForm = $('#auth-form-forgot');
        const resetForm = $('#auth-form-reset');
        const resetCodeDisplay = $('#reset-code-display');
        const resetCodeInput = $('#reset-code');

        if (forgotForm) forgotForm.hidden = true;
        if (resetForm) resetForm.hidden = false;
        if (resetCodeDisplay && res.resetCode) {
          resetCodeDisplay.textContent = res.resetCode;
        }
        if (resetCodeInput && res.resetCode) {
          resetCodeInput.value = res.resetCode;
        }
        showToast('تم إصدار رمز التحقق بنجاح.', 'success');
      } catch (err) {
        if (errorMsg) {
          errorMsg.textContent = err.message || 'تعذر معالجة الطلب.';
          errorMsg.hidden = false;
        }
      } finally {
        isRequestingCode = false;
        if (submitBtn) {
          submitBtn.disabled = false;
          submitBtn.innerHTML = '<span>إرسال رمز التحقق</span>';
        }
      }
    };

    $('#auth-form-forgot')?.addEventListener('submit', executeForgot);
    $('#forgot-submit-btn')?.addEventListener('click', (e) => {
      executeForgot(e);
    });

    // Reset Password (Step 2: Submit New Password)
    let isResetting = false;
    const executeReset = async (e) => {
      if (e) e.preventDefault();
      if (isResetting) return;

      const identifier = $('#forgot-username')?.value.trim();
      const resetCode = $('#reset-code')?.value.trim();
      const newPassword = $('#reset-new-password')?.value;
      const errorMsg = $('#reset-error-msg');
      const submitBtn = $('#reset-submit-btn');

      if (!resetCode) {
        if (errorMsg) {
          errorMsg.textContent = 'يرجى إدخال رمز التحقق.';
          errorMsg.hidden = false;
        }
        return;
      }

      if (!newPassword || newPassword.length < 6) {
        if (errorMsg) {
          errorMsg.textContent = 'كلمة المرور الجديدة يجب ألا تقل عن 6 خانات.';
          errorMsg.hidden = false;
        }
        return;
      }

      try {
        isResetting = true;
        if (errorMsg) errorMsg.hidden = true;
        if (submitBtn) {
          submitBtn.disabled = true;
          submitBtn.innerHTML = '<span>جاري تعيين كلمة المرور...</span>';
        }

        const res = await api('/auth/reset-password', {
          method: 'POST',
          body: JSON.stringify({
            usernameOrEmail: identifier,
            resetCode: resetCode,
            newPassword: newPassword
          })
        });

        showToast(res?.message || 'تم تغيير كلمة المرور بنجاح! يمكنك الآن تسجيل الدخول.', 'success');

        $('#auth-form-reset').reset();
        $('#auth-form-forgot').reset();
        $('#auth-form-reset').hidden = true;
        $('#auth-form-forgot').hidden = false;

        const loginUser = $('#login-username');
        if (loginUser && identifier) loginUser.value = identifier;
        switchAuthTab('login');
      } catch (err) {
        if (errorMsg) {
          errorMsg.textContent = err.message || 'تعذر تغيير كلمة المرور.';
          errorMsg.hidden = false;
        }
      } finally {
        isResetting = false;
        if (submitBtn) {
          submitBtn.disabled = false;
          submitBtn.innerHTML = '<span>تأكيد وتعيين كلمة المرور</span>';
        }
      }
    };

    $('#auth-form-reset')?.addEventListener('submit', executeReset);
    $('#reset-submit-btn')?.addEventListener('click', (e) => {
      executeReset(e);
    });
  }

  // Initialize App
  async function init() {
    initializeTheme();
    initFontSizeResizer();
    initNavScrollArrows();
    setupEventListeners();
    initSettings();
    initQuranAssistant();
    if ($('#view-scratch')) initScratchLearning();
    initReviewWorkspace();
    initLessonChatbot();
    initBiographyModal();
    initTextReader();
    await initAuth();
    await initPdfStorage();
    await loadBooks();
    await loadQuranSurahs();
  }

  function initializeTheme() {
    const savedTheme = localStorage.getItem('bukhari_theme');
    const prefersDark = window.matchMedia?.('(prefers-color-scheme: dark)').matches;
    setTheme(savedTheme || (prefersDark ? 'dark' : 'light'));
  }

  function setTheme(theme) {
    const isDark = theme === 'dark';
    document.documentElement.dataset.theme = isDark ? 'dark' : 'light';

    const toggle = $('#theme-toggle');
    if (!toggle) return;

    toggle.setAttribute('aria-pressed', String(isDark));
    toggle.setAttribute('aria-label', isDark ? 'تفعيل الوضع الفاتح' : 'تفعيل الوضع الداكن');
    toggle.title = isDark ? 'تفعيل الوضع الفاتح' : 'تفعيل الوضع الداكن';
    toggle.querySelector('.theme-toggle-icon').textContent = isDark ? '☀' : '☾';
    toggle.querySelector('.theme-toggle-label').textContent = isDark ? 'الوضع الفاتح' : 'الوضع الداكن';
  }

  // ==========================================================================
  // Navigation Scroll Arrows Manager (أزرار التمرير لشريط التنقل)
  // ==========================================================================

  let updateNavScrollArrowsFn = null;

  function initNavScrollArrows() {
    const nav = $('#app-nav');
    const rightBtn = $('#nav-scroll-right-btn');
    const leftBtn = $('#nav-scroll-left-btn');
    if (!nav || !rightBtn || !leftBtn) return;

    function updateArrowVisibility() {
      const isOverflowing = nav.scrollWidth > nav.clientWidth + 4;
      if (!isOverflowing) {
        rightBtn.hidden = true;
        leftBtn.hidden = true;
        return;
      }

      const maxScroll = nav.scrollWidth - nav.clientWidth;
      const scrollPos = Math.abs(nav.scrollLeft);

      // In RTL, initial right position is scrollPos ~ 0, left boundary is maxScroll
      rightBtn.hidden = scrollPos <= 6;
      leftBtn.hidden = scrollPos >= maxScroll - 6;
    }

    updateNavScrollArrowsFn = updateArrowVisibility;

    rightBtn.addEventListener('click', () => {
      nav.scrollBy({ left: 220, behavior: 'smooth' });
      setTimeout(updateArrowVisibility, 350);
    });

    leftBtn.addEventListener('click', () => {
      nav.scrollBy({ left: -220, behavior: 'smooth' });
      setTimeout(updateArrowVisibility, 350);
    });

    nav.addEventListener('scroll', updateArrowVisibility, { passive: true });
    window.addEventListener('resize', updateArrowVisibility, { passive: true });

    // ResizeObserver for font zooming and container resizing
    if (window.ResizeObserver) {
      const ro = new ResizeObserver(() => updateArrowVisibility());
      ro.observe(nav);
      const wrapper = $('#nav-scroll-wrapper');
      if (wrapper) ro.observe(wrapper);
    }

    // Also update when tabs are clicked or view changes
    $$('.nav-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        setTimeout(updateArrowVisibility, 350);
      });
    });

    setTimeout(updateArrowVisibility, 150);
  }

  // ==========================================================================
  // Font Size Resizing Manager (تكبير وتصغير حجم الخط)
  // ==========================================================================

  const FONT_SCALES = [0.85, 0.92, 1.0, 1.10, 1.20, 1.30, 1.45, 1.60];
  let currentFontScaleIndex = 2; // Default 1.0 (100%)

  function initFontSizeResizer() {
    const savedScale = parseFloat(localStorage.getItem('bukhari_font_scale') || '1.0');
    const matchedIndex = FONT_SCALES.findIndex((s) => Math.abs(s - savedScale) < 0.03);
    if (matchedIndex !== -1) {
      currentFontScaleIndex = matchedIndex;
    }
    applyFontScale(FONT_SCALES[currentFontScaleIndex]);

    $('#font-size-dec-btn')?.addEventListener('click', () => {
      if (currentFontScaleIndex > 0) {
        currentFontScaleIndex--;
        applyFontScale(FONT_SCALES[currentFontScaleIndex]);
      }
    });

    $('#font-size-inc-btn')?.addEventListener('click', () => {
      if (currentFontScaleIndex < FONT_SCALES.length - 1) {
        currentFontScaleIndex++;
        applyFontScale(FONT_SCALES[currentFontScaleIndex]);
      }
    });

    $('#font-size-reset-btn')?.addEventListener('click', () => {
      currentFontScaleIndex = 2; // 100%
      applyFontScale(FONT_SCALES[currentFontScaleIndex]);
    });

    // Keyboard shortcuts: Alt + Plus / Alt + Minus / Alt + 0
    document.addEventListener('keydown', (e) => {
      if (e.altKey && (e.key === '+' || e.key === '=')) {
        e.preventDefault();
        $('#font-size-inc-btn')?.click();
      } else if (e.altKey && (e.key === '-' || e.key === '_')) {
        e.preventDefault();
        $('#font-size-dec-btn')?.click();
      } else if (e.altKey && e.key === '0') {
        e.preventDefault();
        $('#font-size-reset-btn')?.click();
      }
    });
  }

  function applyFontScale(scale) {
    const pct = Math.round(scale * 100);
    document.documentElement.style.fontSize = `${16 * scale}px`;
    document.documentElement.style.setProperty('--font-scale', scale.toString());
    localStorage.setItem('bukhari_font_scale', scale.toString());

    const label = $('#font-size-label');
    if (label) label.textContent = `${pct}%`;

    const decBtn = $('#font-size-dec-btn');
    if (decBtn) decBtn.disabled = currentFontScaleIndex <= 0;

    const incBtn = $('#font-size-inc-btn');
    if (incBtn) incBtn.disabled = currentFontScaleIndex >= FONT_SCALES.length - 1;

    if (typeof updateNavScrollArrowsFn === 'function') {
      setTimeout(updateNavScrollArrowsFn, 100);
    }
  }

  // Event Listeners Registration
  function setupEventListeners() {
    // Navigation bar buttons
    $('#nav-dashboard-btn')?.addEventListener('click', () => switchView('dashboard'));
    $('#nav-scratch-btn')?.addEventListener('click', () => {
      switchView('scratch');
      if (!scratchState.currentRoadmap) {
        loadScratchTrack('mustalah-hadith');
      }
    });
    $('#nav-lessons-btn')?.addEventListener('click', () => switchView('lessons'));
    $('#nav-reviews-btn')?.addEventListener('click', () => {
      switchView('reviews');
      refreshReviewWorkspace();
    });
    $('#nav-upload-btn')?.addEventListener('click', () => {
      switchView('upload');
      syncUploadBookSelector();
    });
    $('#nav-settings-btn')?.addEventListener('click', () => { switchView('settings'); loadSettings(); });
    $('#lessons-add-pages-btn')?.addEventListener('click', () => {
      switchView('upload');
      syncUploadBookSelector();
    });

    // Dashboard Lessons Pagination Arrow Buttons
    $$('.lessons-prev-arrow').forEach((btn) => {
      btn.addEventListener('click', () => {
        if (dashboardLessonsPage > 1) {
          dashboardLessonsPage--;
          renderCurrentLessonsPage();
        }
      });
    });

    $$('.lessons-next-arrow').forEach((btn) => {
      btn.addEventListener('click', () => {
        const maxPage = Math.ceil(currentSortedDashboardLessons.length / LESSONS_PAGE_SIZE);
        if (dashboardLessonsPage < maxPage) {
          dashboardLessonsPage++;
          renderCurrentLessonsPage();
        }
      });
    });

    // Upload Target Book Selector
    $('#upload-target-book-select')?.addEventListener('change', async (e) => {
      const selectedId = e.target.value;
      const isNew = selectedId === '__create_new__';
      const newGroup = $('#new-book-title-group');
      if (newGroup) newGroup.hidden = !isNew;
      if (isNew) {
        $('#book-title-input')?.focus();
        return;
      }

      const found = state.books.find((b) => b.id === selectedId);
      if (found) {
        state.activeBook = found;
        localStorage.setItem('dirayah_selected_book_id', found.id);
        localStorage.setItem('dirayah_default_book_id', found.id);
        const mainSelect = $('#book-select');
        if (mainSelect && mainSelect.value !== found.id) {
          mainSelect.value = found.id;
        }
      }

      await switchBookPdf(selectedId);
    });

    // Page Range Live Validation to Enforce Max 2 Pages
    $('#start-page-input')?.addEventListener('input', () => {
      const startP = parseInt($('#start-page-input').value, 10);
      const endEl = $('#end-page-input');
      if (!isNaN(startP) && startP >= 1 && endEl) {
        endEl.min = startP;
        endEl.max = startP + 1;
        const currentEnd = parseInt(endEl.value, 10);
        if (isNaN(currentEnd) || currentEnd < startP || currentEnd > startP + 1) {
          endEl.value = Math.min(startP + 1, startP + 1);
        }
      }
    });

    $('#end-page-input')?.addEventListener('input', () => {
      const startP = parseInt($('#start-page-input').value, 10) || 1;
      const endEl = $('#end-page-input');
      if (!endEl) return;
      const endP = parseInt(endEl.value, 10);
      if (!isNaN(endP) && (endP - startP + 1) > 2) {
        endEl.value = startP + 1;
        showToast('يُسمح باختيار صفحتين فقط كحد أقصى لضمان استيعاب عميق وتغطية المسائل بدقة.', 'warning');
      }
    });

    // Toast Close
    $('#toast-close')?.addEventListener('click', () => {
      if (state.toastTimer) {
        clearTimeout(state.toastTimer);
        state.toastTimer = null;
      }
      $('#notice-toast').hidden = true;
    });

    $('#theme-toggle')?.addEventListener('click', () => {
      const nextTheme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
      localStorage.setItem('bukhari_theme', nextTheme);
      setTheme(nextTheme);
    });

    // Helper: Extract clean book title from PDF filename
    function extractBookTitleFromFilename(filename) {
      if (!filename) return '';
      let name = filename.replace(/\.[^/.]+$/, ''); // remove extension
      name = name.replace(/[_-]+/g, ' ').replace(/\s+/g, ' ').trim();
      return name;
    }

    // Book Selector Change
    $('#book-select')?.addEventListener('change', async (e) => {
      const selectedId = e.target.value;
      if (selectedId === '__add_new_book__') {
        switchView('upload');
        syncUploadBookSelector();
        const uploadSelect = $('#upload-target-book-select');
        if (uploadSelect) uploadSelect.value = '__create_new__';
        const newGroup = $('#new-book-title-group');
        if (newGroup) newGroup.hidden = false;
        const input = $('#book-title-input');
        if (input) {
          input.value = '';
          input.focus();
        }
        return;
      }
      const found = state.books.find((b) => b.id === selectedId);
      if (found) {
        state.activeBook = found;
        localStorage.setItem('dirayah_selected_book_id', found.id);
        localStorage.setItem('dirayah_default_book_id', found.id);
        syncUploadBookSelector();
        syncSettingsDefaultBookSelector();
        await switchBookPdf(found.id);
        dashboardLessonsPage = 1;
        await refreshWorkspace();
      }
    });

    // Next Action "Continue Learning" Primary CTA
    $('#continue-learning-btn')?.addEventListener('click', handleContinueAction);

    // Reader View Back Button
    $('#reader-back-btn')?.addEventListener('click', () => {
      switchView('dashboard');
      refreshWorkspace();
    });

    // Reader View Go to Assessment CTA
    $('#reader-goto-assessment-btn')?.addEventListener('click', () => {
      if (state.currentLesson) {
        openAssessment(state.currentLesson.id);
      }
    });

    // Assessment View Back Button
    $('#assessment-back-btn')?.addEventListener('click', () => {
      if (state.currentLesson) {
        openLesson(state.currentLesson.id);
      } else {
        switchView('dashboard');
      }
    });

    // Assessment Form Answer Input & Counter
    const answerInput = $('#student-answer-input');
    answerInput?.addEventListener('input', () => {
      const len = answerInput.value.trim().length;
      $('#char-counter').textContent = `${toArabicDigits(len)} حرف`;
    });

    // Assessment Form Submit
    $('#assessment-form')?.addEventListener('submit', handleAnswerSubmit);

    // Next Question Button
    $('#next-question-btn')?.addEventListener('click', handleNextQuestion);

    // Finish Assessment Button
    $('#finish-assessment-btn')?.addEventListener('click', handleFinishAssessment);

    // Source Input Mode Tabs
    $$('.source-tab-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const mode = btn.dataset.sourceMode;
        state.sourceMode = mode;
        $$('.source-tab-btn').forEach((b) => {
          const isActive = b === btn;
          b.classList.toggle('active', isActive);
          b.setAttribute('aria-selected', String(isActive));
        });

        const pdfPanel = $('#source-panel-pdf');
        const textPanel = $('#source-panel-clipboard-text');
        const imagesPanel = $('#source-panel-clipboard-images');

        if (pdfPanel) pdfPanel.hidden = mode !== 'pdf';
        if (textPanel) textPanel.hidden = mode !== 'clipboard-text';
        if (imagesPanel) imagesPanel.hidden = mode !== 'clipboard-images';
      });
    });

    // PDF File Drop / Selection UI with Auto Book-Title Detection & Persistent Retention
    const fileInput = $('#pdf-file-input');
    fileInput?.addEventListener('change', async () => {
      if (fileInput.files && fileInput.files.length > 0) {
        const file = fileInput.files[0];
        const targetBookId = $('#upload-target-book-select')?.value;
        await setPreservedPdf(file, targetBookId);

        const bookTitleInput = $('#book-title-input');
        if (bookTitleInput && !bookTitleInput.value.trim()) {
          bookTitleInput.value = extractBookTitleFromFilename(file.name);
        }
      } else if (state.currentPdfFile) {
        // If user cancelled the file picker dialog, restore the preserved PDF display
        updatePdfDropZoneUI(state.currentPdfFile);
      }
    });

    // Native Drag & Drop Handlers for PDF Drop Zone
    const dropZone = $('#pdf-drop-zone');
    if (dropZone) {
      ['dragenter', 'dragover'].forEach((evt) => {
        dropZone.addEventListener(evt, (e) => {
          e.preventDefault();
          e.stopPropagation();
          dropZone.classList.add('drag-over');
        });
      });

      ['dragleave', 'dragend'].forEach((evt) => {
        dropZone.addEventListener(evt, (e) => {
          e.preventDefault();
          e.stopPropagation();
          dropZone.classList.remove('drag-over');
        });
      });

      dropZone.addEventListener('drop', async (e) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('drag-over');

        const files = e.dataTransfer?.files;
        if (files && files.length > 0) {
          const droppedFile = files[0];
          if (!droppedFile.name.toLowerCase().endsWith('.pdf')) {
            showToast('يرجى اختيار ملف بصيغة PDF فقط.', 'warning');
            return;
          }
          const targetBookId = $('#upload-target-book-select')?.value;
          await setPreservedPdf(droppedFile, targetBookId);

          const bookTitleInput = $('#book-title-input');
          if (bookTitleInput && !bookTitleInput.value.trim()) {
            bookTitleInput.value = extractBookTitleFromFilename(droppedFile.name);
          }
        }
      });
    }

    // Change & Remove Retained PDF Action Buttons
    $('#change-pdf-btn')?.addEventListener('click', (e) => {
      e.stopPropagation();
      $('#pdf-file-input')?.click();
    });

    $('#remove-pdf-btn')?.addEventListener('click', async (e) => {
      e.stopPropagation();
      await clearPreservedPdf();
    });

    // Clipboard Text Paste & Clear Handlers
    function updateTextCounter() {
      const text = ($('#clipboard-source-textarea')?.value || '').trim();
      const words = text ? text.split(/\s+/).length : 0;
      const chars = text.length;
      const el = $('#clipboard-text-counter');
      if (el) {
        el.textContent = `${toArabicDigits(words)} كلمة (${toArabicDigits(chars)} حرف)`;
      }
    }

    $('#clipboard-source-textarea')?.addEventListener('input', updateTextCounter);

    $('#paste-text-btn')?.addEventListener('click', async () => {
      try {
        const text = await navigator.clipboard.readText();
        if (!text || !text.trim()) {
          showToast('الحافظة فارغة أو لا تحتوي على نص.', 'info');
          return;
        }
        const ta = $('#clipboard-source-textarea');
        if (ta) {
          ta.value = text.trim();
          updateTextCounter();
          showToast('تم لصق النص من الحافظة بنجاح ✓', 'success');
        }
      } catch (err) {
        showToast('انقر داخل الحقل واضغط Ctrl + V للصق النص.', 'info');
      }
    });

    $('#clear-text-btn')?.addEventListener('click', () => {
      const ta = $('#clipboard-source-textarea');
      if (ta) {
        ta.value = '';
        updateTextCounter();
      }
    });

    // Clipboard Image Handlers
    function addPastedImage(blob) {
      if (!blob || !blob.type.startsWith('image/')) return;
      const url = URL.createObjectURL(blob);
      const item = { blob, url, id: Date.now() + Math.random() };
      state.pastedImages.push(item);
      renderPastedImages();
      showToast(`تمت إضافة لقطة الشاشة (${toArabicDigits(state.pastedImages.length)}) بنجاح ✓`, 'success');
    }

    function renderPastedImages() {
      const gallery = $('#pasted-images-gallery');
      if (!gallery) return;
      gallery.innerHTML = '';
      if (state.pastedImages.length === 0) {
        gallery.hidden = true;
        return;
      }
      gallery.hidden = false;
      state.pastedImages.forEach((img, idx) => {
        const card = document.createElement('div');
        card.className = 'pasted-image-card';
        card.innerHTML = `
          <img src="${img.url}" class="pasted-image-thumb" alt="صورة ${idx + 1}" />
          <span class="pasted-image-badge">صورة ${toArabicDigits(idx + 1)}</span>
          <button type="button" class="remove-image-btn" data-id="${img.id}" title="حذف الصورة">✕</button>
        `;
        card.querySelector('.remove-image-btn').addEventListener('click', (e) => {
          e.stopPropagation();
          state.pastedImages = state.pastedImages.filter(x => x.id !== img.id);
          renderPastedImages();
        });
        gallery.appendChild(card);
      });
    }

    $('#paste-image-btn')?.addEventListener('click', async () => {
      try {
        const items = await navigator.clipboard.read();
        let foundImage = false;
        for (const item of items) {
          for (const type of item.types) {
            if (type.startsWith('image/')) {
              const blob = await item.getType(type);
              addPastedImage(blob);
              foundImage = true;
            }
          }
        }
        if (!foundImage) {
          showToast('لم يتم العثور على صورة في الحافظة. اضغط Ctrl + V للصق المباشر.', 'info');
        }
      } catch (err) {
        showToast('انقر داخل المربع واضغط Ctrl + V للصق لقطة الشاشة.', 'info');
      }
    });

    const pasteDropzone = $('#image-paste-dropzone');
    pasteDropzone?.addEventListener('paste', (e) => {
      handleClipboardPasteEvent(e);
    });

    window.addEventListener('paste', (e) => {
      if (state.activeView === 'upload' && state.sourceMode === 'clipboard-images') {
        handleClipboardPasteEvent(e);
      }
    });

    function handleClipboardPasteEvent(e) {
      const items = e.clipboardData?.items;
      if (!items) return;
      for (let i = 0; i < items.length; i++) {
        if (items[i].type.indexOf('image') !== -1) {
          const blob = items[i].getAsFile();
          if (blob) {
            addPastedImage(blob);
            e.preventDefault();
          }
        }
      }
    }

    // Upload & Generate Form Submit
    $('#upload-lesson-form')?.addEventListener('submit', handleLessonUpload);
  }

  // Load Registered Books
  async function loadBooks(preferBookId = null) {
    try {
      const books = await api('/books');
      state.books = books || [];

      const select = $('#book-select');
      select.innerHTML = '';

      if (state.books.length === 0) {
        showToast('لا يوجد كتاب مسجل حاليًا. يمكنك إضافة ملف PDF للبدء.', 'info');
        switchView('upload');
        return;
      }

      state.books.forEach((book) => {
        const option = document.createElement('option');
        option.value = book.id;
        const count = book.lessons?.length || 0;
        let countText = 'بدون دروس';
        if (count === 1) countText = 'درس واحد';
        else if (count === 2) countText = 'درسان';
        else if (count >= 3 && count <= 10) countText = `${toArabicDigits(count)} دروس`;
        else if (count > 10) countText = `${toArabicDigits(count)} درس`;

        option.textContent = `📖 ${book.title} (${countText})`;
        select.appendChild(option);
      });

      // Quick Add New Book Option
      const addOption = document.createElement('option');
      addOption.value = '__add_new_book__';
      addOption.textContent = '➕ + إضافة كتاب جديد...';
      select.appendChild(addOption);

      const targetId = preferBookId || localStorage.getItem('dirayah_default_book_id') || localStorage.getItem('dirayah_selected_book_id') || localStorage.getItem('bukhari_selected_book_id');
      state.activeBook = state.books.find((b) => b.id === targetId) || state.books[0];
      select.value = state.activeBook.id;
      localStorage.setItem('dirayah_selected_book_id', state.activeBook.id);
      if (!localStorage.getItem('dirayah_default_book_id')) {
        localStorage.setItem('dirayah_default_book_id', state.activeBook.id);
      }

      syncUploadBookSelector();
      syncSettingsDefaultBookSelector();
      if (state.activeBook) {
        await switchBookPdf(state.activeBook.id);
      }

      await refreshWorkspace();
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  // Synchronize the upload target book selector with registered books
  function syncUploadBookSelector() {
    const uploadSelect = $('#upload-target-book-select');
    if (!uploadSelect) return;
    const currentVal = uploadSelect.value;
    uploadSelect.innerHTML = '';

    (state.books || []).forEach((book) => {
      const opt = document.createElement('option');
      opt.value = book.id;
      opt.textContent = `📖 ${book.title}`;
      uploadSelect.appendChild(opt);
    });

    const newOpt = document.createElement('option');
    newOpt.value = '__create_new__';
    newOpt.textContent = '➕ + إضافة كتاب دراسي جديد...';
    uploadSelect.appendChild(newOpt);

    // If currentVal was already a valid registered book, prioritize preserving it!
    const targetBookId = (currentVal && state.books.some(b => b.id === currentVal))
      ? currentVal
      : (state.activeBook && state.books.some(b => b.id === state.activeBook.id))
        ? state.activeBook.id
        : (localStorage.getItem('dirayah_default_book_id') || localStorage.getItem('dirayah_selected_book_id'));

    if (targetBookId && state.books.some(b => b.id === targetBookId)) {
      uploadSelect.value = targetBookId;
      const newGroup = $('#new-book-title-group');
      if (newGroup) newGroup.hidden = true;
    } else if (state.books.length > 0) {
      uploadSelect.value = state.books[0].id;
    }
  }

  // Synchronize the settings default book selector
  function syncSettingsDefaultBookSelector() {
    const defaultSelect = $('#settings-default-book-select');
    if (!defaultSelect) return;
    defaultSelect.innerHTML = '';

    (state.books || []).forEach((book) => {
      const opt = document.createElement('option');
      opt.value = book.id;
      opt.textContent = `📖 ${book.title}`;
      defaultSelect.appendChild(opt);
    });

    const defaultBookId = localStorage.getItem('dirayah_default_book_id') || state.activeBook?.id;
    if (defaultBookId && state.books.some(b => b.id === defaultBookId)) {
      defaultSelect.value = defaultBookId;
    } else if (state.activeBook) {
      defaultSelect.value = state.activeBook.id;
    }
  }

  // Full Learning Workspace Refresh
  async function refreshWorkspace() {
    if (!state.activeBook) return;

    const bookId = state.activeBook.id;

    try {
      // Parallel fetch of essential backend decisions
      const [nextAction, dashboard, dueReviews, weakConcepts, lessons, learningContext] = await Promise.all([
        api(`/learning/${bookId}/next`).catch(() => null),
        api(`/learning/${bookId}/dashboard`).catch(() => null),
        api(`/learning/${bookId}/due-reviews`).catch(() => []),
        api(`/learning/${bookId}/weak-concepts`).catch(() => []),
        api(`/books/${bookId}/lessons`).catch(() => []),
        api(`/books/${bookId}/learning-context`).catch(() => null)
      ]);

      state.nextAction = nextAction;
      state.dashboard = dashboard;
      state.dueReviews = dueReviews || [];
      state.weakConcepts = weakConcepts || [];
      state.lessons = lessons || [];
      state.learningContext = learningContext;
      state.bookLessons = new Map(await Promise.all(
        state.books.map(async (book) => [book.id, await api(`/books/${book.id}/lessons`).catch(() => [])])
      ));

      renderNextActionCard(nextAction);
      renderDashboardMetrics(dashboard);
      renderDueReviews(dueReviews);
      renderWeakConcepts(weakConcepts);
      renderLessonsList(lessons);
      renderBookLessonGroups();
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  // Render Tutor Next Action Hero Card
  function renderNextActionCard(next) {
    if (!next) {
      $('#next-action-title').textContent = 'لا توجد خطوات حالية';
      $('#next-action-reason').textContent = 'أضف صفحات من الكتاب للبدء في مسار التعلّم.';
      $('#next-action-type').textContent = 'في الانتظار';
      return;
    }

    const actionKey = next.action;
    const def = ACTION_DEFINITIONS[actionKey] || {
      type: 'تعلّم مخصص',
      title: 'تابع التعلّم',
      btnText: 'متابعة التعلّم',
      defaultReason: 'سنختار معًا الخطوة الأنسب لتقدمك الأكاديمي.'
    };

    $('#next-action-type').textContent = def.type;
    $('#next-action-title').textContent = def.title;
    
    // Natural reason translation without internal engine codes
    const reasonText = next.reason && next.reason.trim() ? next.reason : def.defaultReason;
    $('#next-action-reason').textContent = reasonText;

    // Pages badge
    const pagesText = next.sourcePages && next.sourcePages.length > 0
      ? `الصفحات ${next.sourcePages.map(toArabicDigits).join('، ')}`
      : (state.dashboard?.currentPages?.length > 0 ? `الصفحات ${state.dashboard.currentPages.map(toArabicDigits).join('، ')}` : 'المحتوى المتاح');
    $('#next-action-pages .meta-value').textContent = pagesText;

    // Focus / Intent
    const intentMap = {
      0: 'تأسيس درس جديد',
      1: 'متابعة القراءة والتحليل',
      2: 'مراجعة تثبيتية',
      3: 'تقوية المفاهيم الضعيفة',
      'NewLesson': 'تأسيس درس جديد',
      'ContinueLearning': 'متابعة القراءة والتحليل',
      'ReviewDueConcepts': 'مراجعة تثبيتية',
      'ReinforceWeakConcepts': 'تقوية المفاهيم الضعيفة'
    };
    $('#next-action-focus .meta-value').textContent = intentMap[next.learningIntent] || 'مسار موجه';

    // Button label
    const continueBtn = $('#continue-learning-btn');
    continueBtn.querySelector('.btn-text').textContent = def.btnText;
  }

  // Render Academic Progress Metrics
  function renderDashboardMetrics(dashboard) {
    if (!dashboard) return;

    $('#metric-completed-lessons').textContent = toArabicDigits(dashboard.completedLessons || 0);
    $('#metric-understood-concepts').textContent = toArabicDigits(dashboard.understoodConcepts || 0);
    $('#metric-mastered-concepts').textContent = toArabicDigits(dashboard.masteredConcepts || 0);
    $('#metric-weak-concepts').textContent = toArabicDigits(dashboard.weakConcepts || 0);
  }

  // Level display helper
  function getLevelArabicName(level) {
    const levelMap = {
      0: 'مقدم حديثاً',
      1: 'مألوف',
      2: 'مستوعب بالأدلة',
      3: 'متقن ومستقر',
      'Unknown': 'قيد التقييم',
      'Introduced': 'مقدم حديثاً',
      'Familiar': 'مألوف',
      'Understood': 'مستوعب بالأدلة',
      'Mastered': 'متقن ومستقر'
    };
    return levelMap[level] || 'مألوف';
  }

  // Format and clean concept topic titles
  function formatConceptTitle(text) {
    if (!text) return 'مسألة علمية';
    let clean = text.trim().replace(/^([،؛:\-—«»"'\[\]\(\)\{\}\*_\s]+)|([،؛:\-—«»"'\[\]\(\)\{\}\*_\s]+)$/g, '');
    clean = clean.replace(/^(?:و|فـ|ف)(?=[\u0621-\u064A]{3,}\s)/, '').trim();
    if (clean.startsWith('بـ') && clean.length > 3) clean = clean.slice(2).trim();
    if (clean.startsWith('كالـ') && clean.length > 5) clean = 'الـ' + clean.slice(4).trim();
    if (clean.startsWith('كال') && clean.length > 4 && !clean.startsWith('كلام')) clean = 'ال' + clean.slice(3).trim();
    return clean || 'مسألة علمية';
  }

  // Render Due Reviews Panel
  function renderDueReviews(reviews) {
    const list = $('#reviews-list');
    const badge = $('#reviews-badge');
    const summaryDue = $('#reviews-summary-due');

    const count = reviews?.length || 0;
    if (badge) badge.textContent = toArabicDigits(count);
    if (summaryDue) summaryDue.textContent = toArabicDigits(count);

    if (!reviews || reviews.length === 0) {
      list.innerHTML = '<div class="empty-state">لا توجد مراجعات مستحقة الآن. أحسنت في استقرار معلوماتك!</div>';
      return;
    }

    list.innerHTML = reviews.map((rev) => {
      const lessonTargetId = rev.lessonId || '';
      const confidence = typeof rev.confidence === 'number' ? Math.round(rev.confidence * 100) : 50;
      const levelName = getLevelArabicName(rev.currentLearningLevel || rev.learningLevel || 'Understood');
      const conceptTitle = formatConceptTitle(rev.conceptKey);

      return `
        <div class="list-item-card-rich">
          <div class="item-main">
            <div class="item-concept-title">${escapeHtml(conceptTitle)}</div>
            <p class="item-concept-reason">${escapeHtml(rev.reason || 'هذا المفهوم يحتاج إلى مراجعة قصيرة لترسيخه ومنع نسيانه.')}</p>
            <div class="item-concept-meta">
              <span class="pill-badge muted-badge">${levelName}</span>
              <div class="confidence-bar-inline">
                <span>نسبة الثقة:</span>
                <div class="confidence-fill-small">
                  <div class="confidence-fill-inner" style="width: ${confidence}%;"></div>
                </div>
                <strong>${toArabicDigits(confidence)}٪</strong>
              </div>
            </div>
          </div>
          <div class="item-actions-group">
            <button type="button" class="primary-btn small-btn open-smart-reinforce-btn" data-concept-key="${escapeHtml(rev.conceptKey)}" data-lesson-id="${lessonTargetId}">
              <span>🎯 تثبيت ذكي</span>
            </button>
            ${lessonTargetId ? `
              <button type="button" class="subtle-btn small-btn open-review-lesson-btn" data-lesson-id="${lessonTargetId}" title="فتح الدرس الأصلي">
                <span>📖 الدرس</span>
              </button>
            ` : ''}
          </div>
        </div>
      `;
    }).join('');

    $$('.open-smart-reinforce-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const concept = btn.dataset.conceptKey;
        const lessonId = btn.dataset.lessonId;
        openSmartReinforcement(concept, lessonId);
      });
    });

    $$('.open-review-lesson-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const id = btn.dataset.lessonId;
        if (id) openLesson(id);
        else showToast('لا يتوفر درس مرتبط بهذا المفهوم حاليًا.', 'info');
      });
    });
  }

  // Render Weak Concepts Panel
  function renderWeakConcepts(weak) {
    const list = $('#weak-list');
    const badge = $('#weak-badge');
    const summaryWeak = $('#reviews-summary-weak');

    const count = weak?.length || 0;
    if (badge) badge.textContent = toArabicDigits(count);
    if (summaryWeak) summaryWeak.textContent = toArabicDigits(count);

    if (!weak || weak.length === 0) {
      list.innerHTML = '<div class="empty-state">جميع المفاهيم التي اختبرتها حتى الآن مستوعبة ومتقنة بشكل ممتاز.</div>';
      return;
    }

    list.innerHTML = weak.map((c) => {
      const lessonTargetId = c.lastAssessedLessonId || c.firstIntroducedLessonId || '';
      const confidence = typeof c.confidence === 'number' ? Math.round(c.confidence * 100) : 35;
      const levelName = getLevelArabicName(c.learningLevel || 'Familiar');
      const conceptTitle = formatConceptTitle(c.conceptKey);

      return `
        <div class="list-item-card-rich">
          <div class="item-main">
            <div class="item-concept-title">${escapeHtml(conceptTitle)}</div>
            <p class="item-concept-reason">${escapeHtml(c.reason || 'سنشرح هذا المفهوم من زاوية أوضح مع بسط أدلته لتثبيت استيعابك.')}</p>
            <div class="item-concept-meta">
              <span class="pill-badge muted-badge">${levelName}</span>
              <div class="confidence-bar-inline">
                <span>نسبة الثقة:</span>
                <div class="confidence-fill-small">
                  <div class="confidence-fill-inner" style="width: ${confidence}%; background: #EF4444;"></div>
                </div>
                <strong>${toArabicDigits(confidence)}٪</strong>
              </div>
            </div>
          </div>
          <div class="item-actions-group">
            <button type="button" class="primary-btn small-btn open-smart-reinforce-btn" data-concept-key="${escapeHtml(c.conceptKey)}" data-lesson-id="${lessonTargetId}">
              <span>🎯 تثبيت ذكي</span>
            </button>
            ${lessonTargetId ? `
              <button type="button" class="subtle-btn small-btn open-weak-lesson-btn" data-lesson-id="${lessonTargetId}" title="فتح الدرس الأصلي">
                <span>📖 الدرس</span>
              </button>
            ` : ''}
          </div>
        </div>
      `;
    }).join('');

    $$('.open-smart-reinforce-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const concept = btn.dataset.conceptKey;
        const lessonId = btn.dataset.lessonId;
        openSmartReinforcement(concept, lessonId);
      });
    });

    $$('.open-weak-lesson-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const id = btn.dataset.lessonId;
        if (id) openLesson(id);
        else showToast('لا يتوفر درس مسجل لهذا المفهوم حاليًا.', 'info');
      });
    });
  }

  // Render Registered Lessons List (capped to maximum of 6 per page with arrow controls)
  let dashboardLessonsPage = 1;
  const LESSONS_PAGE_SIZE = 6;
  let currentSortedDashboardLessons = [];

  function renderLessonsList(lessons) {
    const dashboardList = $('#dashboard-lessons-list');
    if (!dashboardList) return;

    if (!lessons || lessons.length === 0) {
      currentSortedDashboardLessons = [];
      dashboardLessonsPage = 1;
      updateLessonsPaginationUI(0);
      dashboardList.innerHTML = '<div class="empty-state">لم يتم تسجيل دروس في هذا الكتاب بعد. أضف صفحات لبدء الدرس الأول.</div>';
      return;
    }

    // Place recent lessons on the top (newest first)
    currentSortedDashboardLessons = [...lessons].sort((a, b) => {
      const dateA = a.createdAtUtc ? new Date(a.createdAtUtc).getTime() : 0;
      const dateB = b.createdAtUtc ? new Date(b.createdAtUtc).getTime() : 0;
      if (dateA !== dateB) return dateB - dateA;
      return (b.startPage || 0) - (a.startPage || 0);
    });

    const totalLessons = currentSortedDashboardLessons.length;
    const maxPage = Math.max(1, Math.ceil(totalLessons / LESSONS_PAGE_SIZE));
    if (dashboardLessonsPage > maxPage) dashboardLessonsPage = maxPage;
    if (dashboardLessonsPage < 1) dashboardLessonsPage = 1;

    renderCurrentLessonsPage();
  }

  function renderCurrentLessonsPage() {
    const dashboardList = $('#dashboard-lessons-list');
    if (!dashboardList) return;

    const totalLessons = currentSortedDashboardLessons.length;
    const startIndex = (dashboardLessonsPage - 1) * LESSONS_PAGE_SIZE;
    const pageLessons = currentSortedDashboardLessons.slice(startIndex, startIndex + LESSONS_PAGE_SIZE);

    const lessonCards = pageLessons.map((lesson) => {
      const pagesText = `الصفحات ${toArabicDigits(lesson.startPage)} - ${toArabicDigits(lesson.endPage)}`;
      return `
        <div class="lesson-card">
          <div>
            <div class="lesson-card-header">
              <span class="badge-tag">${pagesText}</span>
            </div>
            <h4 class="lesson-card-title mt-2">${escapeHtml(lesson.title)}</h4>
            <p class="lesson-card-overview">${escapeHtml(lesson.overview || 'لا يتوفر ملخص مختصر.')}</p>
          </div>
          <button class="secondary-btn open-lesson-btn" data-id="${lesson.id}">
            <span>افتح الدرس</span>
            <span aria-hidden="true">←</span>
          </button>
        </div>
      `;
    }).join('');

    dashboardList.innerHTML = lessonCards;

    $$('#dashboard-lessons-list .open-lesson-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        openLesson(btn.dataset.id);
      });
    });

    updateLessonsPaginationUI(totalLessons);
  }

  function updateLessonsPaginationUI(totalLessons) {
    const paginationControls = $$('.lessons-pagination-controls');
    const maxPage = Math.max(1, Math.ceil(totalLessons / LESSONS_PAGE_SIZE));

    paginationControls.forEach((container) => {
      if (totalLessons <= LESSONS_PAGE_SIZE) {
        container.hidden = true;
        return;
      }
      container.hidden = false;
      const prevBtn = container.querySelector('.lessons-prev-arrow');
      const nextBtn = container.querySelector('.lessons-next-arrow');
      const indicator = container.querySelector('.lessons-page-indicator');

      if (prevBtn) prevBtn.disabled = dashboardLessonsPage <= 1;
      if (nextBtn) nextBtn.disabled = dashboardLessonsPage >= maxPage;
      if (indicator) {
        indicator.textContent = `صفحة ${toArabicDigits(dashboardLessonsPage)} من ${toArabicDigits(maxPage)} (${toArabicDigits(totalLessons)} درس)`;
      }
    });
  }

  // Each book keeps an independent collection of lessons in the lessons library.
  function renderBookLessonGroups() {
    const library = $('#lessons-list');
    if (!library) return;

    if (!state.books.length) {
      library.innerHTML = '<div class="empty-state">أضف كتابًا وصفحاته ليظهر هنا.</div>';
      return;
    }

    library.innerHTML = state.books.map((book) => {
      const lessons = state.bookLessons.get(book.id) || [];
      const sortedLessons = [...lessons].sort((a, b) => {
        const dateA = a.createdAtUtc ? new Date(a.createdAtUtc).getTime() : 0;
        const dateB = b.createdAtUtc ? new Date(b.createdAtUtc).getTime() : 0;
        if (dateA !== dateB) return dateB - dateA;
        return (b.startPage || 0) - (a.startPage || 0);
      });
      const cards = sortedLessons.length ? sortedLessons.map((lesson) => {
        const pagesText = `الصفحات ${toArabicDigits(lesson.startPage)} - ${toArabicDigits(lesson.endPage)}`;
        return `<div class="lesson-card"><div><span class="badge-tag">${pagesText}</span><h4 class="lesson-card-title mt-2">${escapeHtml(lesson.title)}</h4><p class="lesson-card-overview">${escapeHtml(lesson.overview || 'لا يتوفر ملخص مختصر.')}</p></div><button class="secondary-btn open-lesson-btn" data-id="${lesson.id}"><span>افتح الدرس</span><span aria-hidden="true">←</span></button></div>`;
      }).join('') : '<div class="empty-state">لا توجد دروس في هذا الكتاب بعد.</div>';

      return `<section class="book-lessons-group"><div class="book-lessons-heading"><h3>${escapeHtml(book.title)}</h3><span>${toArabicDigits(lessons.length)} درس</span></div><div class="lessons-grid">${cards}</div></section>`;
    }).join('');

    $$('.open-lesson-btn').forEach((btn) => btn.addEventListener('click', () => openLesson(btn.dataset.id)));
  }

  // Primary "Continue Learning" Click Dispatcher
  async function handleContinueAction() {
    const next = state.nextAction;
    if (!next) {
      switchView('upload');
      return;
    }

    const action = next.action;

    // Take Assessment Action
    if (action === 'TakeAssessment' || action === 4) {
      if (next.lessonId) {
        return openAssessment(next.lessonId);
      }
    }

    // Direct Reinforce / Review Action -> triggers Smart AI Reinforcement
    if (action === 'ReinforceConcept' || action === 3 || action === 'ReviewConcept' || action === 2) {
      const conceptKey = next.concepts?.[0]?.conceptKey || next.review?.conceptKey;
      if (conceptKey) {
        return openSmartReinforcement(conceptKey, next.lessonId);
      }
    }

    // Direct Lesson Action
    if (next.lessonId) {
      return openLesson(next.lessonId);
    }

    // Source Progression Action
    if (action === 'ContinueToNextPages' || action === 6 || action === 'Completed' || action === 7) {
      switchView('upload');
      $('#upload-end-notice').hidden = false;
      return;
    }

    // If StartNewLesson or other action without explicit lessonId
    if (state.lessons.length > 0) {
      return openLesson(state.lessons[0].id);
    }

    switchView('upload');
  }

  // ==========================================================================
  // Lesson Reader Implementation (Progressive Disclosure)
  // ==========================================================================
  async function openLesson(lessonId) {
    if (!lessonId) return;

    try {
      const [lesson, progress] = await Promise.all([
        api(`/lessons/${lessonId}`),
        api(`/lessons/${lessonId}/progress`).catch(() => null)
      ]);

      state.currentLesson = lesson;
      state.lessonProgress = progress;

      // Update Reading state to InProgress if not already
      if (!progress || progress.status === 0 || progress.status === 'NotStarted') {
        api(`/lessons/${lessonId}/progress`, {
          method: 'PUT',
          body: JSON.stringify({ status: 1 }) // 1 = InProgress / Reading
        }).catch(() => null);
      }

      renderLessonReader(lesson, progress);
      switchView('reader');
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  // Extract and organize authentic Arabic page texts from lesson
  function extractLessonPageTexts(lesson) {
    if (!lesson) return [];
    const pageMap = new Map();
    const startP = lesson.startPage || 1;
    const endP = lesson.endPage || startP;

    const hadiths = lesson.hadiths || [];
    hadiths.forEach((hadith, hIdx) => {
      const ref = hadith.reference || `المقطع ${toArabicDigits(hIdx + 1)}`;
      if (hadith.evidences && hadith.evidences.length > 0) {
        hadith.evidences.forEach((ev) => {
          if (!ev.text || !ev.text.trim()) return;
          let pageNumbers = [];
          if (ev.sourcePagesCsv) {
            const parts = ev.sourcePagesCsv.split(/[,،-]/).map(s => parseInt(s.trim(), 10)).filter(n => !isNaN(n));
            if (parts.length > 0) pageNumbers = [...new Set(parts)];
          }
          if (pageNumbers.length === 0) pageNumbers = [startP];

          pageNumbers.forEach(pNum => {
            if (!pageMap.has(pNum)) pageMap.set(pNum, []);
            pageMap.get(pNum).push({
              text: ev.text.trim(),
              role: ev.role || 'متن الحديث الشريف',
              reference: ref,
              hadithIndex: hIdx
            });
          });
        });
      }
    });

    if (lesson.lessonPages && lesson.lessonPages.length > 0) {
      lesson.lessonPages.forEach(lp => {
        const pNum = lp.bookPage?.pageNumber || lp.pageNumber;
        const text = lp.bookPage?.extractedText;
        if (text && text.trim() && pNum) {
          if (!pageMap.has(pNum)) pageMap.set(pNum, []);
          const existing = pageMap.get(pNum);
          if (!existing.some(e => e.text.includes(text.trim().substring(0, 30)))) {
            existing.push({
              text: text.trim(),
              role: 'نص مستخرج من الصفحة',
              reference: `صفحة ${toArabicDigits(pNum)}`,
              hadithIndex: -1
            });
          }
        }
      });
    }

    if (pageMap.size === 0 && hadiths.length > 0) {
      hadiths.forEach((hadith, hIdx) => {
        const pNum = startP;
        if (!pageMap.has(pNum)) pageMap.set(pNum, []);
        const text = hadith.problem || hadith.easyExplanation || hadith.summary;
        if (text) {
          pageMap.get(pNum).push({
            text: text.trim(),
            role: 'المتن والمسألة',
            reference: hadith.reference || `المقطع ${toArabicDigits(hIdx + 1)}`,
            hadithIndex: hIdx
          });
        }
      });
    }

    const sortedPages = Array.from(pageMap.entries()).sort((a, b) => a[0] - b[0]);
    return sortedPages.map(([pageNumber, items]) => ({
      pageNumber,
      items
    }));
  }

  function renderLessonReader(lesson, progress) {
    // Header & Meta
    const eyebrowEl = $('#reader-book-eyebrow');
    if (eyebrowEl) {
      eyebrowEl.textContent = `${state.activeBook?.title || 'كتاب دراسي'} — درس تعليمي`;
    }
    $('#reader-lesson-title').textContent = lesson.title;
    $('#reader-lesson-overview').textContent = lesson.overview || `يتناول هذا الدرس المسائل العلمية والأدلة المستخرجة من ${state.activeBook?.title || 'الكتاب'}.`;

    const pagesText = `الصفحات ${toArabicDigits(lesson.startPage)} - ${toArabicDigits(lesson.endPage)}`;
    $('#reader-source-pages').textContent = pagesText;

    const isCompleted = progress?.status === 2 || progress?.status === 'Completed';
    $('#reader-status-badge').textContent = isCompleted ? 'مكتمل ومستوعب' : 'قيد القراءة والتحليل';
    $('#reader-status-badge').classList.toggle('status-tag', isCompleted);

    // 0. Overview Card
    const overviewCard = $('.lesson-overview-card');
    if (overviewCard) {
      overviewCard.id = 'reader-overview-card';
    }

    // 1. Render Original Arabic Pages Section (#reader-original-pages-section)
    const pagesSection = $('#reader-original-pages-section');
    const pagesList = $('#reader-pages-list');
    const pagesCountBadge = $('#reader-pages-count-badge');
    const extractedPages = extractLessonPageTexts(lesson);

    if (pagesSection && pagesList) {
      if (extractedPages && extractedPages.length > 0) {
        if (pagesCountBadge) {
          const totalSnippets = extractedPages.reduce((acc, p) => acc + p.items.length, 0);
          pagesCountBadge.textContent = `${toArabicDigits(extractedPages.length)} ${extractedPages.length === 1 ? 'صفحة' : 'صفحات'} (${toArabicDigits(totalSnippets)} نصوص)`;
        }

        pagesList.innerHTML = extractedPages.map(p => `
          <div class="page-text-card" id="reader-page-card-${p.pageNumber}">
            <div class="page-text-card-header">
              <div class="page-badge-wrap">
                <span class="panel-icon" aria-hidden="true">📄</span>
                <strong class="page-title">صفحة ${toArabicDigits(p.pageNumber)}</strong>
                <span class="pill-badge muted-badge">${toArabicDigits(p.items.length)} نصوص أصلية</span>
              </div>
            </div>
            <div class="page-text-card-body">
              ${p.items.map((item, itemIdx) => `
                <div class="page-evidence-item" id="reader-page-item-${p.pageNumber}-${itemIdx}">
                  <div class="page-evidence-meta">
                    <span class="evidence-role-badge">${escapeHtml(item.role)}</span>
                    <span class="pill-badge muted-badge">${escapeHtml(item.reference)}</span>
                  </div>
                  <blockquote class="arabic-page-text">« ${escapeHtml(item.text)} »</blockquote>
                </div>
              `).join('')}
            </div>
          </div>
        `).join('');

        pagesSection.hidden = false;
      } else {
        pagesSection.hidden = true;
      }
    }

    // 2. Render Hadiths / Scientific Breakdown
    const container = $('#reader-hadiths-container');
    container.innerHTML = '';

    const hadiths = lesson.hadiths || [];
    hadiths.forEach((hadith, index) => {
      const card = document.createElement('div');
      card.className = 'hadith-card';
      card.id = `hadith-card-${index}`;

      // 1. Reference & Citation
      const refTitle = hadith.reference || `المقطع ${toArabicDigits(index + 1)}`;
      const refHtml = `
        <div class="hadith-card-header-actions">
          <div class="hadith-ref-badge">${escapeHtml(refTitle)}</div>
        </div>
      `;

      // 2. Problem / Scholarly Issue
      const problemHtml = hadith.problem ? `
        <div class="hadith-problem-box">
          <div class="hadith-problem-heading">المسألة الفقهية / موضوع المقطع:</div>
          <p class="hadith-problem-text">${escapeHtml(hadith.problem)}</p>
        </div>
      ` : '';

      // 3. Simple Explanation (Beginner friendly with inline terms in parentheses)
      const easyExpText = hadith.easyExplanation || hadith.summary || 'سيظهر الشرح الميسر هنا.';
      const easyExpHtml = `
        <div class="easy-explanation-box">
          <div class="reasoning-heading">الشرح التفصيلي للمقطع:</div>
          ${formatDifficultTerms(escapeHtml(easyExpText))}
        </div>
      `;

      // 4. Evidence Items
      let evidencesHtml = '';
      if (hadith.evidences && hadith.evidences.length > 0) {
        const items = hadith.evidences.map((ev, evIdx) => `
          <div class="evidence-item" id="hadith-${index}-evidence-${evIdx}">
            <div class="evidence-header-row">
              <div>
                ${ev.role ? `<span class="evidence-role-badge">${escapeHtml(ev.role)}</span>` : ''}
                ${ev.sourcePagesCsv ? `<span class="pill-badge muted-badge">صفحة ${escapeHtml(ev.sourcePagesCsv)}</span>` : ''}
              </div>
            </div>
            <p class="evidence-text">« ${escapeHtml(ev.text)} »</p>
          </div>
        `).join('');

        evidencesHtml = `
          <div class="evidence-section">
            <h4 class="evidence-heading">الأدلة والاستدلال:</h4>
            ${items}
          </div>
        `;
      }

      // 5. Inferential Reasoning
      const reasoningHtml = hadith.reasoning ? `
        <div class="reasoning-box">
          <div class="reasoning-heading">وجه الدلالة والمسار الاستدلالي:</div>
          <p class="reasoning-text">${escapeHtml(hadith.reasoning)}</p>
        </div>
      ` : '';

      // 6. Scholarly Dispute (Collapsible progressive disclosure)
      const discussionHtml = hadith.scholarlyDiscussion && hadith.scholarlyDiscussion.trim() ? `
        <details class="scholarly-discussion-details">
          <summary>الخلاف الفقهي وسياق المسألة (انقر للتفصيل)</summary>
          <div class="scholarly-discussion-content">
            <p>${escapeHtml(hadith.scholarlyDiscussion)}</p>
          </div>
        </details>
      ` : '';

      // 7. Final Ruling / Conclusion
      const conclusionHtml = hadith.conclusion ? `
        <div class="conclusion-box">
          <div class="conclusion-heading">النتيجة الفقهية والخلاصة:</div>
          <p class="conclusion-text">${escapeHtml(hadith.conclusion)}</p>
        </div>
      ` : '';

      card.innerHTML = refHtml + problemHtml + easyExpHtml + evidencesHtml + reasoningHtml + discussionHtml + conclusionHtml;

      container.appendChild(card);
    });

    // 8. People Section (Distinguishing Familiar vs Newly Introduced)
    renderPeopleSection(lesson);

    // 9. Connections & General Takeaways
    const connectionsCard = $('#reader-connections-card');
    const connectionsList = $('#reader-connections-list');
    if (lesson.connections && lesson.connections.length > 0) {
      connectionsList.innerHTML = lesson.connections.map((c) => `<li>${escapeHtml(c.description)}</li>`).join('');
      connectionsCard.hidden = false;
    } else {
      connectionsCard.hidden = true;
    }

    // 10. Self-Review Questions Before Assessment
    const reviewCard = $('#reader-review-questions-card');
    const reviewList = $('#reader-review-questions-list');
    if (lesson.reviewQuestions && lesson.reviewQuestions.length > 0) {
      reviewList.innerHTML = lesson.reviewQuestions.map((q) => `<li>${escapeHtml(q.question)}</li>`).join('');
      reviewCard.hidden = false;
    } else {
      reviewCard.hidden = true;
    }
  }

  // Format inline difficult terms with subtle emphasis: e.g. الدباغ (معالجة جلد الحيوان...)
  function formatDifficultTerms(text) {
    return text.replace(/([\u0621-\u064A\s]+)\(([\u0621-\u064A\s\w]+)\)/g, (match, term, explanation) => {
      return `<strong class="difficult-term">${term.trim()}</strong> <span class="term-explanation">(${explanation.trim()})</span>`;
    });
  }

  // Render People with Active Educational Memory
  function renderPeopleSection(lesson) {
    const section = $('#reader-people-section');
    const grid = $('#reader-people-list');

    const lessonPeople = lesson.lessonPeople || [];
    if (lessonPeople.length === 0) {
      section.hidden = true;
      return;
    }

    section.hidden = false;
    const knownPeopleList = state.learningContext?.knownPeople || [];

    grid.innerHTML = lessonPeople.map((lp) => {
      const personName = lp.person?.name || 'علم';
      const personDesc = lp.person?.description || '';
      const contextDesc = lp.contextDescription || personDesc;

      // Check if known in educational memory
      const known = knownPeopleList.find((kp) => kp.personId === lp.personId || (kp.person && kp.person.name === personName));
      const isFamiliar = known && (known.timesSeen > 1 || known.learningLevel > 0);

      if (isFamiliar) {
        // Familiar person: short contextual reminder + clickable biography
        return `
          <div class="person-badge-card familiar-person" data-person-name="${escapeHtml(personName)}" data-context-desc="${escapeHtml(contextDesc)}" tabindex="0" role="button" aria-label="عرض ترجمة وسيرة ${escapeHtml(personName)} من سير أعلام النبلاء">
            <span class="person-status-tag">شخصية مرت بك سابقًا</span>
            <h5 class="person-name">${escapeHtml(personName)}</h5>
            <p class="person-bio">${escapeHtml(contextDesc || 'سبق التعريف به في الدروس السابقة.')}</p>
            <div class="person-card-action">
              <span>📖 عرض السيرة من سير أعلام النبلاء</span>
              <span class="action-arrow">⇽</span>
            </div>
          </div>
        `;
      } else {
        // New person: full introductory profile + clickable biography
        return `
          <div class="person-badge-card new-person" data-person-name="${escapeHtml(personName)}" data-context-desc="${escapeHtml(contextDesc)}" tabindex="0" role="button" aria-label="عرض ترجمة وسيرة ${escapeHtml(personName)} من سير أعلام النبلاء">
            <span class="person-status-tag">شخصية جديدة في هذا الموضع</span>
            <h5 class="person-name">${escapeHtml(personName)}</h5>
            <p class="person-bio"><strong>التعريف:</strong> ${escapeHtml(personDesc || contextDesc)}</p>
            ${contextDesc && contextDesc !== personDesc ? `<p class="person-bio mt-1"><strong>سياقه في الدرس:</strong> ${escapeHtml(contextDesc)}</p>` : ''}
            <div class="person-card-action">
              <span>📖 عرض السيرة من سير أعلام النبلاء</span>
              <span class="action-arrow">⇽</span>
            </div>
          </div>
        `;
      }
    }).join('');

    // Attach click and keyboard handlers to all person cards
    grid.querySelectorAll('.person-badge-card').forEach((card) => {
      const pName = card.getAttribute('data-person-name');
      const pContext = card.getAttribute('data-context-desc');

      card.addEventListener('click', () => {
        if (pName) openPersonBiography(pName, pContext);
      });

      card.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          if (pName) openPersonBiography(pName, pContext);
        }
      });
    });
  }

  // ==========================================================================
  // Person Biography from Siyar A'lam al-Nubala
  // ==========================================================================
  async function openPersonBiography(personName, contextDesc = '', forceRefresh = false) {
    if (!personName) return;

    const modal = $('#person-biography-modal');
    const loadingBox = $('#bio-modal-loading');
    const errorBox = $('#bio-modal-error');
    const contentBox = $('#bio-modal-content');

    if (!modal) return;

    // Reset view state
    modal.hidden = false;
    document.body.style.overflow = 'hidden';
    if (loadingBox) loadingBox.hidden = false;
    if (errorBox) errorBox.hidden = true;
    if (contentBox) contentBox.hidden = true;

    // Set Header placeholders
    const nameEl = $('#bio-modal-person-name');
    const epithetEl = $('#bio-modal-person-epithet');
    const eraEl = $('#bio-modal-era-badge');
    const deathEl = $('#bio-modal-death-badge');

    if (nameEl) nameEl.textContent = personName;
    if (epithetEl) epithetEl.textContent = contextDesc ? contextDesc.slice(0, 100) : 'عَلَم من الأعلام — جارٍ استحضار الترجمة الموثقة...';
    if (eraEl) eraEl.textContent = 'عصر الرواية';
    if (deathEl) deathEl.textContent = 'سير أعلام النبلاء';

    state.activeBiographyPerson = { name: personName, contextDesc };

    try {
      const payload = {
        name: personName,
        contextDescription: contextDesc || null,
        lessonId: state.currentLesson?.id || null,
        bookId: state.currentBook?.id || null,
        forceRefresh: !!forceRefresh
      };

      const data = await api('/lessons/people/biography', {
        method: 'POST',
        body: JSON.stringify(payload)
      });

      state.activeBiography = data;
      renderBiographyModalContent(data);

      if (loadingBox) loadingBox.hidden = true;
      if (contentBox) contentBox.hidden = false;
    } catch (err) {
      console.error('Failed to load person biography:', err);
      if (loadingBox) loadingBox.hidden = true;
      if (errorBox) errorBox.hidden = false;
      const errText = $('#bio-modal-error-text');
      if (errText) errText.textContent = err.message || 'تعذر استحضار ترجمة الشخصية من سير أعلام النبلاء حالياً.';
    }
  }

  function renderBiographyModalContent(data) {
    const pName = data.name || state.activeBiographyPerson?.name || 'علم';
    const nameEl = $('#bio-modal-person-name');
    if (nameEl) nameEl.textContent = pName;

    // Epithet & Kunya
    let epithet = data.title || '';
    if (data.kunya) {
      epithet = epithet ? `${epithet} — (${data.kunya})` : data.kunya;
    }
    const epithetEl = $('#bio-modal-person-epithet');
    if (epithetEl) epithetEl.textContent = epithet || 'عَلَم وراوٍ من رجال الحديث والتاريخ';

    // Era & Death
    const eraEl = $('#bio-modal-era-badge');
    const deathEl = $('#bio-modal-death-badge');
    if (eraEl) eraEl.textContent = data.era || 'عصر الرواية والحديث';
    if (deathEl) deathEl.textContent = data.deathYear || 'تاريخ الوفاة في كتب التراجم';

    // Summary
    const summaryText = data.summary || state.activeBiographyPerson?.contextDesc || 'ترجمة تعريفية موجزة.';
    const summaryEl = $('#bio-modal-summary-text');
    if (summaryEl) summaryEl.textContent = summaryText;

    // Siyar narrative
    const siyarHtml = (data.siyarBiography || '')
      .split('\n\n')
      .filter(Boolean)
      .map((p) => `<p>${escapeHtml(p.trim())}</p>`)
      .join('') || `<p>${escapeHtml(summaryText)}</p>`;
    const siyarEl = $('#bio-modal-siyar-text');
    if (siyarEl) siyarEl.innerHTML = siyarHtml;

    // Teachers & Students
    const teachersList = $('#bio-modal-teachers-list');
    const studentsList = $('#bio-modal-students-list');
    const dualGrid = $('#bio-modal-teachers-students-grid');

    const teachers = data.teachers || [];
    const students = data.students || [];

    if (teachersList) {
      if (teachers.length > 0) {
        teachersList.innerHTML = teachers.map((t) => `<span class="bio-chip">${escapeHtml(t)}</span>`).join('');
      } else {
        teachersList.innerHTML = `<span class="text-muted">مذكورون في أمهات كتب الرجال والرواية.</span>`;
      }
    }

    if (studentsList) {
      if (students.length > 0) {
        studentsList.innerHTML = students.map((s) => `<span class="bio-chip">${escapeHtml(s)}</span>`).join('');
      } else {
        studentsList.innerHTML = `<span class="text-muted">مذكورون في أمهات كتب الرجال والرواية.</span>`;
      }
    }

    if (dualGrid) {
      dualGrid.hidden = teachers.length === 0 && students.length === 0;
    }

    // Scholarly Praise
    const praiseCard = $('#bio-modal-praise-card');
    const praiseList = $('#bio-modal-praise-list');
    const praises = data.scholarlyPraise || [];

    if (praiseCard && praiseList) {
      if (praises.length > 0) {
        praiseList.innerHTML = praises.map((p) => `
          <div class="praise-quote-item">
            <div class="praise-scholar">قول ${escapeHtml(p.scholar || 'الإمام')}:</div>
            <p class="praise-text">« ${escapeHtml(p.quote || '')} »</p>
          </div>
        `).join('');
        praiseCard.hidden = false;
      } else {
        praiseCard.hidden = true;
      }
    }

    // Virtues & Narrations
    const virtuesCard = $('#bio-modal-virtues-card');
    const virtuesText = $('#bio-modal-virtues-text');
    if (virtuesCard && virtuesText) {
      if (data.virtuesAndNarrations && data.virtuesAndNarrations.trim()) {
        virtuesText.textContent = data.virtuesAndNarrations;
        virtuesCard.hidden = false;
      } else {
        virtuesCard.hidden = true;
      }
    }

    // Sources
    const sourcesList = $('#bio-modal-sources-list');
    if (sourcesList) {
      const sources = data.sources && data.sources.length > 0
        ? data.sources
        : ['سير أعلام النبلاء — الإمام الذهبي', 'تهذيب الكمال — الحافظ المزي', 'الإصابة في تمييز الصحابة — ابن حجر'];

      sourcesList.innerHTML = sources.map((s) => `<span class="source-pill-tag">📚 ${escapeHtml(s)}</span>`).join('');
    }
  }

  function closePersonBiographyModal() {
    const modal = $('#person-biography-modal');
    if (modal) {
      modal.hidden = true;
      document.body.style.overflow = '';
    }
  }

  function initBiographyModal() {
    $('#close-biography-modal-btn')?.addEventListener('click', closePersonBiographyModal);
    $('#bio-close-footer-btn')?.addEventListener('click', closePersonBiographyModal);

    $('#person-biography-modal')?.addEventListener('click', (e) => {
      if (e.target.id === 'person-biography-modal') {
        closePersonBiographyModal();
      }
    });

    $('#bio-refresh-btn')?.addEventListener('click', () => {
      if (state.activeBiographyPerson) {
        openPersonBiography(state.activeBiographyPerson.name, state.activeBiographyPerson.contextDesc, true);
      }
    });

    $('#bio-modal-retry-btn')?.addEventListener('click', () => {
      if (state.activeBiographyPerson) {
        openPersonBiography(state.activeBiographyPerson.name, state.activeBiographyPerson.contextDesc, true);
      }
    });

    $('#bio-ask-tutor-btn')?.addEventListener('click', () => {
      const pName = state.activeBiography?.name || state.activeBiographyPerson?.name || 'هذه الشخصية';
      closePersonBiographyModal();
      openLessonChat(state.currentLesson?.id || null);
      const prompt = `حدثني بتفصيل علمي موثق عن ترجمة ${pName} من سير أعلام النبلاء ومكانته ومروياته وسياق ذكره في هذا الباب.`;
      const input = $('#lesson-chat-input');
      if (input) {
        input.value = prompt;
        input.focus();
      }
      sendChatMessage(prompt);
    });

    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        const bioModal = $('#person-biography-modal');
        if (bioModal && !bioModal.hidden) {
          closePersonBiographyModal();
        }
      }
    });
  }

  // ==========================================================================
  // Text-to-Speech (TTS) - Disabled & Neutralized
  // ==========================================================================
  const BukhariTextReader = {
    isPlaying: false,
    stop() {},
    init() {},
    readCard() {},
    readPages() {},
    readSinglePage() {},
    readAll() {}
  };

  function initTextReader() {}

  // ==========================================================================
  // Assessment Experience (One Question at a Time)
  // ==========================================================================
  async function openAssessment(lessonId) {
    try {
      const assessments = await api(`/assessments/by-lesson/${lessonId}`);
      const assessment = assessments && assessments.length > 0 ? assessments[0] : null;

      if (!assessment || !assessment.questions || assessment.questions.length === 0) {
        showToast('لا توجد أسئلة تقييم مسجلة لهذا الدرس حاليًا.', 'info');
        return;
      }

      state.activeAssessment = {
        lessonId: lessonId,
        ...assessment
      };
      state.currentQuestionIndex = 0;
      state.assessmentResults = [];

      $('#assessment-lesson-name').textContent = state.currentLesson?.title || '';
      renderQuestionStep();
      switchView('assessment');
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  function renderQuestionStep() {
    const assessment = state.activeAssessment;
    const index = state.currentQuestionIndex;
    const questions = assessment.questions;
    const q = questions[index];

    // Top progress
    const total = questions.length;
    $('#assessment-step-count').textContent = `سؤال ${toArabicDigits(index + 1)} من ${toArabicDigits(total)}`;
    const progressPercent = Math.round((index / total) * 100);
    $('#assessment-progress-fill').style.width = `${progressPercent}%`;

    // Question badges
    const typeMap = {
      'ConceptualExplanation': 'شرح واستيعاب المفاهيم',
      'EvidenceAnalysis': 'تحليل الأدلة والاستدلال',
      'RulingsAndApplication': 'الأحكام والتطبيق',
      'ScholarlyDispute': 'الخلاف والترجيح'
    };
    $('#question-type-badge').textContent = typeMap[q.questionType] || q.questionType || 'فهم واستدلال';

    const diffMap = {
      'Beginner': 'مستوى ميسر',
      'Intermediate': 'مستوى متوسط',
      'Advanced': 'مستوى متقدم'
    };
    $('#question-diff-badge').textContent = diffMap[q.difficulty] || 'مستوى متوسط';

    const pagesText = q.sourcePages && q.sourcePages.length > 0 ? `صفحة ${q.sourcePages.map(toArabicDigits).join('، ')}` : 'المقطع الحالي';
    $('#question-pages-badge').textContent = pagesText;

    // Prompt & Guidance
    $('#question-text').textContent = q.question;
    $('#question-guidance-hint').textContent = q.evaluationGuidance ? `إرشاد: ركز على استيعاب المسألة مع بيان الأدلة.` : 'بيّن فهمك للمسألة بأسلوبك الخاص.';

    // Reset Form
    const input = $('#student-answer-input');
    input.value = '';
    input.disabled = false;
    $('#char-counter').textContent = '٠ حرف';
    $('#submit-answer-btn').disabled = false;
    $('#submit-answer-btn .btn-text').textContent = 'إرسال الإجابة للتقييم';
    $('#submit-answer-btn .spinner').hidden = true;

    // Hide previous feedback
    $('#assessment-feedback-card').hidden = true;
  }

  async function handleAnswerSubmit(e) {
    e.preventDefault();

    const input = $('#student-answer-input');
    const answer = input.value.trim();

    if (!answer) {
      showToast('يرجى كتابة إجابتك قبل الإرسال.', 'warning');
      return;
    }

    const q = state.activeAssessment.questions[state.currentQuestionIndex];
    const submitBtn = $('#submit-answer-btn');

    // UI Loading state
    input.disabled = true;
    submitBtn.disabled = true;
    submitBtn.querySelector('.btn-text').textContent = 'جاري تقييم الإجابة...';
    submitBtn.querySelector('.spinner').hidden = false;

    try {
      const response = await api('/assessments/submit-answer', {
        method: 'POST',
        body: JSON.stringify({
          questionId: q.id,
          studentAnswer: answer,
          submissionId: crypto.randomUUID()
        })
      });

      state.assessmentResults.push(response);
      renderEvaluationFeedback(response);
    } catch (err) {
      input.disabled = false;
      submitBtn.disabled = false;
      submitBtn.querySelector('.btn-text').textContent = 'إعادة الإرسال';
      submitBtn.querySelector('.spinner').hidden = true;
      showToast(err.message, 'error');
    }
  }

  function renderEvaluationFeedback(result) {
    const card = $('#assessment-feedback-card');
    card.hidden = false;

    // Status Pill
    const pill = $('#feedback-status-pill');
    pill.className = 'status-pill';

    const statusMap = {
      0: { text: 'تحتاج لمزيد من الدقة والاستدلال', cls: 'insufficient' },
      1: { text: 'إجابة جيدة مع ملاحظات مساعدة', cls: 'partial' },
      2: { text: 'استيعاب ممتاز ومتقن', cls: 'correct' },
      'InsufficientEvidence': { text: 'تحتاج لمزيد من الدقة والاستدلال', cls: 'insufficient' },
      'NeedsImprovement': { text: 'تحتاج لمزيد من الدقة والتوضيح', cls: 'partial' },
      'PartiallyCorrect': { text: 'إجابة جيدة مع ملاحظات مساعدة', cls: 'partial' },
      'Correct': { text: 'استيعاب ممتاز ومتقن للأدلة', cls: 'correct' }
    };

    const statusInfo = statusMap[result.answerStatus] || { text: 'تم تقييم الإجابة', cls: 'correct' };
    pill.textContent = statusInfo.text;
    pill.classList.add(statusInfo.cls);

    // Learning Level
    const levelMap = {
      0: 'مقدم',
      1: 'مألوف',
      2: 'مستوعب ومفهوم',
      3: 'متقن ومتمكن',
      'Unknown': 'قيد التقييم',
      'Introduced': 'مستوى تمهيدي',
      'Familiar': 'مستوى مألوف',
      'Understood': 'مستوى مستوعب',
      'Mastered': 'مستوى متقن'
    };
    $('#feedback-level-pill').textContent = `المستوى: ${levelMap[result.level] || 'مستوعب'}`;

    // Evaluator Comments
    $('#feedback-text').textContent = result.feedback || 'أحسنت في بيان وجه المسألة والاستدلال عليها.';

    // Understood Concepts Tag List
    const understoodGroup = $('#understood-concepts-group');
    const understoodList = $('#understood-concepts-list');
    if (result.understoodConcepts && result.understoodConcepts.length > 0) {
      understoodList.innerHTML = result.understoodConcepts.map((c) => `<span class="concept-tag">✓ ${escapeHtml(c)}</span>`).join('');
      understoodGroup.hidden = false;
    } else {
      understoodGroup.hidden = true;
    }

    // Missing / Guidance Points
    const missingGroup = $('#missing-concepts-group');
    const missingList = $('#missing-concepts-list');
    const points = [...(result.missingConcepts || []), ...(result.misconceptions || [])];
    if (points.length > 0) {
      missingList.innerHTML = points.map((p) => `<span class="concept-tag">💡 ${escapeHtml(p)}</span>`).join('');
      missingGroup.hidden = false;
    } else {
      missingGroup.hidden = true;
    }

    // Button Next vs Finish
    const isLast = state.currentQuestionIndex >= state.activeAssessment.questions.length - 1;
    $('#next-question-btn').hidden = isLast;
    $('#finish-assessment-btn').hidden = !isLast;

    card.scrollIntoView({ behavior: 'smooth' });
  }

  function handleNextQuestion() {
    state.currentQuestionIndex++;
    renderQuestionStep();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  async function handleFinishAssessment() {
    const lessonId = state.activeAssessment?.lessonId;

    if (lessonId) {
      // Mark Lesson as Completed in progress
      await api(`/lessons/${lessonId}/progress`, {
        method: 'PUT',
        body: JSON.stringify({ status: 2 }) // 2 = Completed
      }).catch(() => null);
    }

    showToast('تم إتمام التقييم وتحديث الذاكرة التعليمية بنجاح.', 'success', 'أحسنت!');

    // Transition back to Workspace and reload next authoritative action
    switchView('dashboard');
    await refreshWorkspace();
  }

  // ==========================================================================
  // Source Progression & Content Ingestion Flow (PDF / Text / Images)
  // ==========================================================================
  async function handleLessonUpload(e) {
    e.preventDefault();

    const mode = state.sourceMode || 'pdf';
    const uploadBookSelect = $('#upload-target-book-select');
    const selectedModeBook = uploadBookSelect ? uploadBookSelect.value : state.activeBook?.id;
    let targetBookId = state.activeBook?.id;
    let newBookTitle = '';

    if (selectedModeBook === '__create_new__') {
      newBookTitle = $('#book-title-input')?.value?.trim() || '';
      if (!newBookTitle) {
        showToast('يرجى كتابة اسم الكتاب الجديد.', 'warning');
        $('#book-title-input')?.focus();
        return;
      }
    } else if (selectedModeBook) {
      targetBookId = selectedModeBook;
    }

    const startPage = parseInt($('#start-page-input').value, 10) || 1;
    const endPage = parseInt($('#end-page-input').value, 10) || startPage;

    if (isNaN(startPage) || isNaN(endPage) || startPage < 1 || endPage < startPage) {
      showToast('يرجى إدخال نطاق صفحات / مقاطع صحيح وموجب.', 'warning');
      return;
    }

    if ((endPage - startPage + 1) > 2) {
      showToast('يُسمح باختيار صفحتين فقط كحد أقصى لضمان استيعاب عميق وتغطية المسائل بدقة.', 'warning');
      return;
    }

    let file = null;
    let pastedText = '';

    if (mode === 'pdf') {
      file = $('#pdf-file-input').files[0] || (targetBookId ? state.bookPdfs.get(targetBookId) : null) || state.currentPdfFile;
      if (!file) {
        showToast('يرجى اختيار ملف PDF أولاً.', 'warning');
        return;
      }
    } else if (mode === 'clipboard-text') {
      pastedText = ($('#clipboard-source-textarea')?.value || '').trim();
      if (!pastedText) {
        showToast('يرجى لصق أو كتابة نص المقطع الدراسي أولاً.', 'warning');
        $('#clipboard-source-textarea')?.focus();
        return;
      }
    } else if (mode === 'clipboard-images') {
      if (state.pastedImages.length === 0) {
        showToast('يرجى لصق صورة أو لقطة شاشة واحدة على الأقل من الحافظة (Ctrl + V).', 'warning');
        return;
      }
    }

    if (newBookTitle) {
      try {
        const book = await api('/books', {
          method: 'POST',
          body: JSON.stringify({ title: newBookTitle })
        });
        targetBookId = book.id;
        localStorage.setItem('dirayah_default_book_id', book.id);
        localStorage.setItem('dirayah_selected_book_id', book.id);
        if (file) {
          state.bookPdfs.set(book.id, file);
          await savePdfToStorage(book.id, file);
        }
        await loadBooks(book.id);
      } catch (err) {
        showToast(err.message, 'error');
        return;
      }
    }

    // UI Loading state
    const submitBtn = $('#generate-lesson-btn');
    const statusCard = $('#generation-status-card');
    submitBtn.disabled = true;
    submitBtn.querySelector('.spinner').hidden = false;
    statusCard.hidden = false;

    try {
      let response;

      if (mode === 'pdf') {
        const form = new FormData();
        form.append('pdf', file);
        form.append('startPage', startPage);
        form.append('endPage', endPage);
        if (targetBookId) form.append('bookId', targetBookId);

        response = await fetch('/api/lessons/generate', {
          method: 'POST',
          headers: { 'ngrok-skip-browser-warning': 'true' },
          body: form
        });
      } else if (mode === 'clipboard-text') {
        response = await fetch('/api/lessons/generate-from-text', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'ngrok-skip-browser-warning': 'true'
          },
          body: JSON.stringify({
            sourceText: pastedText,
            startPage,
            endPage,
            bookId: targetBookId
          })
        });
      } else if (mode === 'clipboard-images') {
        const form = new FormData();
        state.pastedImages.forEach((img, idx) => {
          form.append('images', img.blob, `screenshot_${idx + 1}.png`);
        });
        form.append('startPage', startPage);
        form.append('endPage', endPage);
        if (targetBookId) form.append('bookId', targetBookId);

        response = await fetch('/api/lessons/generate-from-images', {
          method: 'POST',
          headers: { 'ngrok-skip-browser-warning': 'true' },
          body: form
        });
      }

      if (!response.ok) {
        if (response.status === 429) {
          throw new Error('الخدمة مشغولة حاليًا بسبب قيود سعة المعالجة المؤقتة. يُرجى الانتظار دقيقة واحدة ثم إعادة المحاولة.');
        }
        const prob = await response.json().catch(() => null);
        throw new Error(prob?.detail || 'تعذر معالجة المقطع وإنشاء الدرس.');
      }

      const lessonData = await response.json();
      const targetBookName = state.books.find(b => b.id === targetBookId)?.title || state.activeBook?.title || 'الكتاب';
      showToast(`تم إنشاء الدرس وحفظه في كتاب "${targetBookName}" بنجاح.`, 'success');

      // Preserve the target book as active and default
      if (targetBookId) {
        state.activeBook = state.books.find(b => b.id === targetBookId) || state.activeBook;
        localStorage.setItem('dirayah_default_book_id', targetBookId);
        localStorage.setItem('dirayah_selected_book_id', targetBookId);
      }

      // Synchronize upload book selector and ensure target book remains selected
      syncUploadBookSelector();
      if (targetBookId) {
        const upSelect = $('#upload-target-book-select');
        if (upSelect) upSelect.value = targetBookId;
        const newGroup = $('#new-book-title-group');
        if (newGroup) newGroup.hidden = true;
      }

      // Preserve PDF file in memory & IndexedDB and keep drop zone UI showing it
      if (file) {
        if (targetBookId) {
          state.bookPdfs.set(targetBookId, file);
          await savePdfToStorage(targetBookId, file);
        }
        state.currentPdfFile = file;
        updatePdfDropZoneUI(file);
      }

      // Auto-advance start and end page inputs for the subsequent lesson (next 2 pages)
      const nextStart = endPage + 1;
      const nextEnd = nextStart + 1; // exactly the next 2 pages
      const startIn = $('#start-page-input');
      const endIn = $('#end-page-input');
      if (startIn) startIn.value = nextStart;
      if (endIn) {
        endIn.min = nextStart;
        endIn.max = nextStart + 1;
        endIn.value = nextEnd;
      }

      // Reset alternative clipboard text/image inputs only
      const textTa = $('#clipboard-source-textarea');
      if (textTa) textTa.value = '';
      const textCounter = $('#clipboard-text-counter');
      if (textCounter) textCounter.textContent = '٠ كلمات';

      state.pastedImages.forEach(img => URL.revokeObjectURL(img.url));
      state.pastedImages = [];
      const gallery = $('#pasted-images-gallery');
      if (gallery) {
        gallery.innerHTML = '';
        gallery.hidden = true;
      }

      // Reload books to update lesson counts and active state
      await loadBooks(targetBookId);

      // Re-ensure the file and dropzone UI remain active and preserved after book reload
      if (file || state.currentPdfFile) {
        const activeFile = file || state.currentPdfFile;
        state.currentPdfFile = activeFile;
        if (targetBookId) {
          state.bookPdfs.set(targetBookId, activeFile);
        }
        state.bookPdfs.set('__last_active__', activeFile);
        updatePdfDropZoneUI(activeFile);
        try {
          const fileInput = $('#pdf-file-input');
          if (fileInput && (!fileInput.files || fileInput.files.length === 0)) {
            const dt = new DataTransfer();
            dt.items.add(activeFile);
            fileInput.files = dt.files;
          }
        } catch (_) {}
      }

      // Re-ensure next two pages are ready in inputs
      if (startIn) startIn.value = nextStart;
      if (endIn) {
        endIn.min = nextStart;
        endIn.max = nextStart + 1;
        endIn.value = nextEnd;
      }

      // Show continuous creation banner with shortcut to view lesson if desired
      const continuousBanner = $('#upload-continuous-banner');
      const continuousTitle = $('#upload-continuous-title');
      const continuousDesc = $('#upload-continuous-desc');
      const viewLessonBtn = $('#upload-view-lesson-btn');
      const generatedLessonId = lessonData?.lessonId || lessonData?.id || (state.lessons && state.lessons[0]?.id);

      if (continuousBanner) {
        if (continuousTitle) {
          continuousTitle.textContent = `تم إنشاء وحفظ الدرس للصفحات (${toArabicDigits(startPage)} - ${toArabicDigits(endPage)}) بنجاح!`;
        }
        if (continuousDesc) {
          continuousDesc.innerHTML = `تم إبقاء ملف الكتاب معتمداً وتجهيز الصفحتين التاليتين تلقائياً (<strong>${toArabicDigits(nextStart)} - ${toArabicDigits(nextEnd)}</strong>).<br>اضغط مباشرة على <strong>معالجة المصدر وإنشاء الدرس</strong> بالأسفل لإنشاء الدرس التالي فوراً دون الحاجة لاختيار الكتاب أو الصفحات مجدداً!`;
        }
        continuousBanner.hidden = false;
        continuousBanner.scrollIntoView({ behavior: 'smooth', block: 'nearest' });

        if (viewLessonBtn) {
          viewLessonBtn.onclick = () => {
            if (generatedLessonId) openLesson(generatedLessonId);
            else switchView('lessons');
          };
        }
      }
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      submitBtn.disabled = false;
      submitBtn.querySelector('.spinner').hidden = true;
      statusCard.hidden = true;
    }
  }


  // ═══════════════════════════════════════════════════════════
  // SETTINGS PAGE
  // ═══════════════════════════════════════════════════════════

  let _defaultSettings = null; // cache of server defaults

  async function loadSettings() {
    try {
      const res = await fetch('/api/settings', {
        headers: {
          'Accept': 'application/json',
          'ngrok-skip-browser-warning': 'true'
        }
      });
      if (!res.ok) throw new Error('Failed to load settings');
      const data = await res.json();
      _defaultSettings = data;

      const customPromptTA = $('#settings-custom-prompt');
      const keyInput       = $('#settings-fallback-key');
      const modelInput     = $('#settings-fallback-model');
      const assessTA       = $('#settings-assessment-prompt');
      const lessonTA       = $('#settings-lesson-prompt');

      if (customPromptTA) customPromptTA.value = data['AI:CustomResponseInstructions'] || '';
      if (keyInput)       keyInput.value       = data['AI:FallbackApiKey']             || '';
      if (modelInput)     modelInput.value     = data['AI:FallbackModel']              || 'claude-sonnet-4-5';
      if (assessTA)       assessTA.value       = data['AI:AssessmentSystemPrompt']     || '';
      if (lessonTA)       lessonTA.value       = data['AI:LessonSystemPrompt']         || '';

      syncSettingsDefaultBookSelector();
    } catch (err) {
      console.error('loadSettings error:', err);
    }
  }

  async function saveSettings(e) {
    e.preventDefault();
    const saveBtn    = $('#settings-save-btn');
    const spinner    = saveBtn.querySelector('.spinner');
    const noticeEl   = $('#settings-save-notice');

    saveBtn.disabled = true;
    spinner.hidden   = false;
    noticeEl.hidden  = true;

    // Save and apply default book choice
    const defaultBookId = $('#settings-default-book-select')?.value;
    if (defaultBookId) {
      localStorage.setItem('dirayah_default_book_id', defaultBookId);
      localStorage.setItem('dirayah_selected_book_id', defaultBookId);
      const bookObj = state.books.find(b => b.id === defaultBookId);
      if (bookObj) {
        state.activeBook = bookObj;
        const mainSelect = $('#book-select');
        if (mainSelect) mainSelect.value = defaultBookId;
        dashboardLessonsPage = 1;
        refreshWorkspace();
      }
    }

    const payload = {
      'AI:CustomResponseInstructions': ($('#settings-custom-prompt')?.value || '').trim(),
      'AI:FallbackApiKey':             ($('#settings-fallback-key')?.value || '').trim(),
      'AI:FallbackModel':              ($('#settings-fallback-model')?.value || '').trim(),
      'AI:AssessmentSystemPrompt':     ($('#settings-assessment-prompt')?.value || '').trim(),
      'AI:LessonSystemPrompt':         ($('#settings-lesson-prompt')?.value || '').trim(),
    };

    try {
      const res = await fetch('/api/settings', {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          'ngrok-skip-browser-warning': 'true'
        },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.error || 'فشل حفظ الإعدادات');
      }

      showNotice('تم حفظ الإعدادات وتثبيت الكتاب الافتراضي والبرومبت المخصص بنجاح ✓', 'success');
    } catch (err) {
      showNotice(err.message, 'error');
    } finally {
      saveBtn.disabled = false;
      spinner.hidden   = true;
    }
  }

  function showNotice(msg, type) {
    const el = $('#settings-save-notice');
    if (!el) return;
    el.textContent = msg;
    el.className   = `settings-notice ${type}`;
    el.hidden      = false;
    setTimeout(() => { el.hidden = true; }, 4000);
  }

  function initSettings() {
    const form      = $('#settings-form');
    const resetBtn  = $('#settings-reset-btn');
    const toggleBtn = $('#toggle-key-visibility');

    if (form)     form.addEventListener('submit', saveSettings);
    if (resetBtn) resetBtn.addEventListener('click', () => {
      if (_defaultSettings) {
        const customPromptTA = $('#settings-custom-prompt');
        if (customPromptTA) customPromptTA.value = '';
        $('#settings-fallback-key').value   = '';
        $('#settings-fallback-model').value = _defaultSettings['AI:FallbackModel'] || 'claude-sonnet-4-5';
        $('#settings-assessment-prompt').value = _defaultSettings['AI:AssessmentSystemPrompt'] || '';
        $('#settings-lesson-prompt').value     = _defaultSettings['AI:LessonSystemPrompt'] || '';
        showNotice('تم إعادة التعيين للقيم الافتراضية — اضغط حفظ لتطبيقها.', 'success');
      }
    });
    if (toggleBtn) toggleBtn.addEventListener('click', () => {
      const keyEl = $('#settings-fallback-key');
      keyEl.type = keyEl.type === 'password' ? 'text' : 'password';
    });

    $('#settings-default-book-select')?.addEventListener('change', (e) => {
      const bookId = e.target.value;
      if (bookId) {
        localStorage.setItem('dirayah_default_book_id', bookId);
        localStorage.setItem('dirayah_selected_book_id', bookId);
        const bookObj = state.books.find(b => b.id === bookId);
        if (bookObj) {
          state.activeBook = bookObj;
          const mainSelect = $('#book-select');
          if (mainSelect) mainSelect.value = bookId;
          dashboardLessonsPage = 1;
          refreshWorkspace();
        }
      }
    });

    // Preset chips click handlers
    document.querySelectorAll('.preset-chip').forEach(btn => {
      btn.addEventListener('click', () => {
        const presetText = btn.getAttribute('data-preset');
        const customPromptTA = $('#settings-custom-prompt');
        if (customPromptTA && presetText) {
          customPromptTA.value = presetText;
          customPromptTA.focus();
          showNotice('تم وضع النموذج المختار في الحقل — اضغط حفظ الإعدادات لتفعيله.', 'success');
        }
      });
    });
  }

  // ═══════════════════════════════════════════════════════════
  // QURAN MEMORIZATION & TADABBUR ASSISTANT
  // ═══════════════════════════════════════════════════════════

  async function loadQuranSurahs() {
    try {
      const surahs = await api('/quran/surahs');
      state.quranSurahs = surahs || [];

      const populateSelect = (el) => {
        if (!el) return;
        el.innerHTML = '';
        state.quranSurahs.forEach((s) => {
          const opt = document.createElement('option');
          opt.value = s.number;
          opt.textContent = `${toArabicDigits(s.number)}. سورة ${s.name} (${s.revelationType} - ${toArabicDigits(s.totalAyat)} آية)`;
          el.appendChild(opt);
        });
      };

      populateSelect($('#quran-surah-select'));
      populateSelect($('#dashboard-quran-select'));

      // Default to Surah 67 (الملك) or Surah 1 (الفاتحة)
      const defaultSurah = state.quranSurahs.find(s => s.number === 67) || state.quranSurahs[0];
      if (defaultSurah) {
        if ($('#quran-surah-select')) $('#quran-surah-select').value = defaultSurah.number;
        if ($('#dashboard-quran-select')) $('#dashboard-quran-select').value = defaultSurah.number;
        updateAyahRangeInputs(defaultSurah);
      }
    } catch (err) {
      console.error('Failed to load Quran surahs:', err);
    }
  }

  function updateAyahRangeInputs(surah) {
    if (!surah) return;
    const startInput = $('#quran-start-ayah');
    const endInput = $('#quran-end-ayah');
    const fullCheck = $('#quran-full-surah-check');

    if (fullCheck && fullCheck.checked) {
      if (startInput) { startInput.value = '1'; startInput.disabled = true; }
      if (endInput) { endInput.value = surah.totalAyat; endInput.placeholder = surah.totalAyat; endInput.disabled = true; }
    } else {
      if (startInput) startInput.disabled = false;
      if (endInput) { endInput.placeholder = surah.totalAyat; endInput.disabled = false; }
    }
  }

  function initQuranAssistant() {
    // Nav Button
    $('#nav-quran-btn')?.addEventListener('click', () => switchView('quran'));

    // Dashboard Quran Buttons
    $('#dashboard-open-quran-btn')?.addEventListener('click', () => switchView('quran'));

    $('#dashboard-quran-start-btn')?.addEventListener('click', () => {
      const selectedNum = parseInt($('#dashboard-quran-select')?.value, 10) || 67;
      startQuranStudyForSurah(selectedNum);
    });

    // Quick Surah Chips in Dashboard
    $$('.quick-surah-chip').forEach(btn => {
      btn.addEventListener('click', () => {
        const surahNum = parseInt(btn.getAttribute('data-surah'), 10);
        if (surahNum) {
          startQuranStudyForSurah(surahNum);
        }
      });
    });

    // Surah select change
    $('#quran-surah-select')?.addEventListener('change', (e) => {
      const num = parseInt(e.target.value, 10);
      const found = state.quranSurahs.find(s => s.number === num);
      updateAyahRangeInputs(found);
    });

    // Full surah checkbox
    $('#quran-full-surah-check')?.addEventListener('change', () => {
      const num = parseInt($('#quran-surah-select')?.value, 10);
      const found = state.quranSurahs.find(s => s.number === num);
      updateAyahRangeInputs(found);
    });

    // Toggle custom text
    $('#toggle-quran-custom-text-btn')?.addEventListener('click', () => {
      const wrapper = $('#quran-custom-text-wrapper');
      if (wrapper) {
        wrapper.hidden = !wrapper.hidden;
        if (!wrapper.hidden) $('#quran-custom-text')?.focus();
      }
    });

    // Quran section navigation: all study sections stay available on one page;
    // the buttons provide a quick jump without concealing any result.
    $$('.quran-tab-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const tabId = btn.dataset.quranTab;
        state.activeQuranTab = tabId;

        $$('.quran-tab-btn').forEach((b) => {
          const isActive = b === btn;
          b.classList.toggle('active', isActive);
          b.setAttribute('aria-selected', String(isActive));
        });

        const panes = {
          'connections': $('#quran-tab-connections'),
          'thematics': $('#quran-tab-thematics'),
          'mutashabihat': $('#quran-tab-mutashabihat'),
          'tadabbur': $('#quran-tab-tadabbur')
        };

        Object.values(panes).forEach(pane => { if (pane) pane.hidden = false; });
        panes[tabId]?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      });
    });

    // Quran Form Submit
    $('#quran-analysis-form')?.addEventListener('submit', handleQuranAnalysisSubmit);
  }

  function startQuranStudyForSurah(surahNum) {
    switchView('quran');
    const select = $('#quran-surah-select');
    if (select) {
      select.value = surahNum;
      const found = state.quranSurahs.find(s => s.number === surahNum);
      updateAyahRangeInputs(found);
    }
    const fullCheck = $('#quran-full-surah-check');
    if (fullCheck) fullCheck.checked = true;

    // Trigger analysis
    $('#quran-analyze-btn')?.click();
  }

  async function handleQuranAnalysisSubmit(e) {
    e.preventDefault();

    const surahNum = parseInt($('#quran-surah-select')?.value, 10);
    const selectedSurah = state.quranSurahs.find(s => s.number === surahNum);
    const isFull = $('#quran-full-surah-check')?.checked;
    const startAyah = isFull ? 1 : parseInt($('#quran-start-ayah')?.value, 10) || 1;
    const endAyah = isFull ? (selectedSurah?.totalAyat || null) : (parseInt($('#quran-end-ayah')?.value, 10) || null);
    const customText = ($('#quran-custom-text')?.value || '').trim();

    const payload = {
      surahNumber: surahNum || null,
      surahName: selectedSurah?.name || null,
      startAyah: startAyah,
      endAyah: endAyah,
      customText: customText || null
    };

    const submitBtn = $('#quran-analyze-btn');
    const loadingCard = $('#quran-loading-card');
    const resultsContainer = $('#quran-results-container');

    submitBtn.disabled = true;
    submitBtn.querySelector('.spinner').hidden = false;
    loadingCard.hidden = false;
    resultsContainer.hidden = true;

    try {
      const data = await api('/quran/analyze', {
        method: 'POST',
        body: JSON.stringify(payload)
      });

      state.activeQuranAnalysis = data;
      renderQuranResults(data);
      resultsContainer.hidden = false;

      const toastMsg = data.isExistingLessonUpdated
        ? `تم تحديث درس "${data.surahInfo?.name || 'السورة'}" المحفوظ في مساحة التعلم بنجاح!`
        : `تم حفظ دراسة "${data.surahInfo?.name || 'السورة'}" كدرس في مساحة التعلم بنجاح!`;

      showToast(toastMsg, 'success');
      resultsContainer.scrollIntoView({ behavior: 'smooth' });

      // Refresh books and dashboard in background so the new/updated lesson is visible everywhere
      await loadBooks(state.activeBook?.id);
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      submitBtn.disabled = false;
      submitBtn.querySelector('.spinner').hidden = true;
      loadingCard.hidden = true;
    }
  }

  // ==========================================================================
  // Quran Analysis & Memorization Engine
  // ==========================================================================
  let activeQuranAudio = null;
  let activeAudioButton = null;

  function pad3(num) {
    return String(num || 1).padStart(3, '0');
  }

  function getMemorizedAyahsKey(surahNumber) {
    return `bukhari_quran_memorized_${surahNumber || 1}`;
  }

  function getMemorizedAyahs(surahNumber) {
    try {
      const raw = localStorage.getItem(getMemorizedAyahsKey(surahNumber));
      return raw ? new Set(JSON.parse(raw)) : new Set();
    } catch {
      return new Set();
    }
  }

  function saveMemorizedAyahs(surahNumber, set) {
    try {
      localStorage.setItem(getMemorizedAyahsKey(surahNumber), JSON.stringify(Array.from(set)));
    } catch (e) {
      console.error(e);
    }
  }

  function toggleAyahMemorizedState(surahNumber, ayahNumber) {
    const set = getMemorizedAyahs(surahNumber);
    if (set.has(ayahNumber)) {
      set.delete(ayahNumber);
    } else {
      set.add(ayahNumber);
    }
    saveMemorizedAyahs(surahNumber, set);
    updateSurahMemorizationProgressDisplay(surahNumber);
    return set.has(ayahNumber);
  }

  function updateSurahMemorizationProgressDisplay(surahNumber) {
    const total = state.activeQuranAnalysis?.surahInfo?.totalAyat
      || state.activeQuranAnalysis?.ayahAnalyses?.length
      || 30;
    const memorized = getMemorizedAyahs(surahNumber);
    const count = memorized.size;
    const percent = total > 0 ? Math.min(100, Math.round((count / total) * 100)) : 0;

    const statsEl = $('#surah-progress-stats');
    const fillEl = $('#surah-progress-bar-fill');
    if (statsEl) {
      statsEl.textContent = `تم حفظ ${toArabicDigits(count)} من ${toArabicDigits(total)} آية (${toArabicDigits(percent)}%)`;
    }
    if (fillEl) {
      fillEl.style.width = `${percent}%`;
    }

    // Update all button states in ayah list
    document.querySelectorAll('.ayah-memorize-btn').forEach((btn) => {
      const aNum = Number(btn.getAttribute('data-ayah-number'));
      const isMem = memorized.has(aNum);
      btn.classList.toggle('is-memorized', isMem);
      btn.innerHTML = isMem ? `<span>✅ تم الحفظ والإتقان</span>` : `<span>⬜ وضع علامة تم الحفظ</span>`;
    });

    // Update recitation toggle button if active
    const reciteBtn = $('#recite-toggle-memorized-btn');
    if (reciteBtn && state.recitationAyahNumber) {
      const isMem = memorized.has(state.recitationAyahNumber);
      reciteBtn.innerHTML = isMem ? `<span>✅ تم حفظ هذه الآية</span>` : `<span>⬜ وضع علامة تم الحفظ</span>`;
    }
  }

  function stopActiveQuranAudio() {
    if (activeQuranAudio) {
      activeQuranAudio.pause();
      activeQuranAudio = null;
    }
    if (activeAudioButton) {
      activeAudioButton.classList.remove('playing');
      if (activeAudioButton.id === 'quran-listen-all-btn') {
        const iconSpan = activeAudioButton.querySelector('.btn-icon');
        const textSpan = activeAudioButton.querySelector('.btn-text');
        if (iconSpan) iconSpan.textContent = '🎙️';
        if (textSpan) textSpan.textContent = 'استماع لتلاوة السورة كاملة';
      } else if (activeAudioButton.classList.contains('ayah-audio-btn')) {
        activeAudioButton.innerHTML = '<span>▶️ استمع للآية</span>';
      }
      activeAudioButton = null;
    }
  }

  function playFullSurahAudio(surahNumber, surahName, buttonEl) {
    if (activeQuranAudio && activeAudioButton === buttonEl) {
      stopActiveQuranAudio();
      return;
    }

    stopActiveQuranAudio();

    const sPad = pad3(surahNumber);
    const audioUrl = `https://server8.mp3quran.net/afs/${sPad}.mp3`;
    const fallbackUrl = `https://download.quranicaudio.com/quran/mishaari_raashid_al_3afaasee/${sPad}.mp3`;

    const audio = new Audio(audioUrl);
    activeQuranAudio = audio;
    activeAudioButton = buttonEl;

    const iconSpan = buttonEl?.querySelector('.btn-icon');
    const textSpan = buttonEl?.querySelector('.btn-text');

    if (buttonEl) {
      buttonEl.classList.add('playing');
      if (iconSpan) iconSpan.textContent = '⏸️';
      if (textSpan) textSpan.textContent = `إيقاف تلاوة سورة ${surahName || ''}`.trim();
    }

    const resetButton = () => {
      if (buttonEl) {
        buttonEl.classList.remove('playing');
        if (iconSpan) iconSpan.textContent = '🎙️';
        if (textSpan) textSpan.textContent = 'استماع لتلاوة السورة كاملة';
      }
      if (activeQuranAudio === audio) {
        activeQuranAudio = null;
        activeAudioButton = null;
      }
    };

    audio.onended = () => {
      resetButton();
      showToast(`اكتملت تلاوة سورة ${surahName || ''} المباركة`, 'success');
    };

    let triedFallback = false;
    audio.onerror = () => {
      if (!triedFallback) {
        triedFallback = true;
        audio.src = fallbackUrl;
        audio.play().catch(() => {
          resetButton();
          showToast('تعذر تشغيل التلاوة الصوتية للسورة، يرجى التحقق من اتصال الإنترنت.', 'error');
        });
      } else {
        resetButton();
        showToast('تعذر تشغيل التلاوة الصوتية للسورة، يرجى التحقق من اتصال الإنترنت.', 'error');
      }
    };

    audio.play().then(() => {
      showToast(`بدأ تشغيل تلاوة سورة ${surahName || ''} كاملة بصوت الشيخ مشاري العفاسي`, 'info');
    }).catch(() => {
      resetButton();
    });
  }

  function playAyahAudio(surahNumber, ayahNumber, buttonEl) {
    if (activeQuranAudio && activeAudioButton === buttonEl) {
      stopActiveQuranAudio();
      return;
    }

    stopActiveQuranAudio();

    const sPad = pad3(surahNumber);
    const aPad = pad3(ayahNumber);
    const audioUrl = `https://everyayah.com/data/Alafasy_128kbps/${sPad}${aPad}.mp3`;

    const audio = new Audio(audioUrl);
    activeQuranAudio = audio;
    activeAudioButton = buttonEl;

    if (buttonEl) {
      buttonEl.classList.add('playing');
      buttonEl.innerHTML = '<span>⏸️ إيقاف التلاوة</span>';
    }

    const resetButton = () => {
      if (buttonEl) {
        buttonEl.classList.remove('playing');
        buttonEl.innerHTML = '<span>▶️ استمع للآية</span>';
      }
      if (activeQuranAudio === audio) {
        activeQuranAudio = null;
        activeAudioButton = null;
      }
    };

    audio.onended = () => {
      resetButton();
    };

    audio.onerror = () => {
      showToast('تعذر تشغيل التلاوة الصوتية للآية، يرجى التحقق من اتصال الإنترنت.', 'error');
      resetButton();
    };

    audio.play().catch(() => {
      resetButton();
    });
  }

  function renderQuranResults(data) {
    if (!data) return;

    // 1. Hero Card
    const info = data.surahInfo || {};
    const surahNum = info.number || 1;
    $('#surah-number-badge').textContent = `سورة رقم ${toArabicDigits(info.number || '')}`;
    $('#surah-type-badge').textContent = info.revelationType || 'مكية';
    $('#surah-ayat-badge').textContent = `${toArabicDigits(info.totalAyat || '')} آية`;
    $('#surah-range-badge').textContent = `النطاق: ${info.analyzedRange || 'كامل السورة'}`;
    $('#surah-display-title').textContent = info.name || 'سورة كريمة';
    $('#surah-objective-text').textContent = info.mainObjective || 'مقصد السورة العام والوحدة الموضوعية.';

    // Virtues & Names Box
    const virtuesBox = $('#surah-virtues-box');
    const namesText = $('#surah-names-text');
    const virtuesText = $('#surah-virtues-text');
    const planRow = $('#surah-plan-row');
    const planText = $('#surah-plan-text');

    const namesList = info.names || (info.name ? [info.name] : []);
    const virtuesList = info.virtues || data.virtues || [];
    const planStr = info.memorizationPlan || '';

    if (virtuesBox) {
      if (namesList.length > 0 || virtuesList.length > 0 || planStr) {
        virtuesBox.hidden = false;
        if (namesText) namesText.textContent = namesList.join(' • ');
        if (virtuesText) virtuesText.textContent = virtuesList.length > 0 ? virtuesList.join(' | ') : 'سورة مباركة عظيمة الشأن في القرآن الكريم.';
        if (planRow && planText) {
          if (planStr) {
            planRow.hidden = false;
            planText.textContent = planStr;
          } else {
            planRow.hidden = true;
          }
        }
      } else {
        virtuesBox.hidden = true;
      }
    }

    // Progress Bar
    updateSurahMemorizationProgressDisplay(surahNum);

    // Hero Action Buttons
    const listenAllBtn = $('#quran-listen-all-btn');
    if (listenAllBtn) {
      const iconSpan = listenAllBtn.querySelector('.btn-icon');
      const textSpan = listenAllBtn.querySelector('.btn-text');
      if (iconSpan) iconSpan.textContent = '🎙️';
      if (textSpan) textSpan.textContent = 'استماع لتلاوة السورة كاملة';
      listenAllBtn.classList.remove('playing');
      listenAllBtn.onclick = () => {
        playFullSurahAudio(surahNum, info.name || '', listenAllBtn);
      };
    }

    $('#quran-ask-tutor-btn')?.addEventListener('click', () => {
      openLessonChat(data.lessonId || null);
      const prompt = `حدثني عن سورة ${info.name || ''}، وما هي مقاصدها الرئيسة وهداياتها العملية وأسرار فواصلها وتناسب آياتها؟`;
      const input = $('#lesson-chat-input');
      if (input) {
        input.value = prompt;
        input.focus();
      }
      sendChatMessage(prompt);
    });

    $('#quran-copy-summary-btn')?.addEventListener('click', () => {
      const lines = [
        `🕋 **${info.name || 'السورة'}** (${info.revelationType || 'مكية'} - ${info.totalAyat || 30} آية)`,
        `🎯 **المقصد العام**: ${info.mainObjective || ''}`,
        info.virtues?.length ? `✨ **الفضائل**: ${info.virtues.join(' - ')}` : '',
        '',
        `📑 **المحاور الموضوعية**:`,
        ...(data.thematicSections || []).map(s => `- ${s.title} (${s.ayahRange}): ${s.summary}`),
        '',
        `💡 **أبرز التدبرات والهدايات**:`,
        ...(data.coreReflections || []).map(r => `- ${r}`)
      ].filter(Boolean).join('\n');

      navigator.clipboard.writeText(lines).then(() => {
        showToast('تم نسخ ملخص دراسة السورة للحفظ والتدبر بنجاح!', 'success');
      }).catch(() => {
        showToast('تعذر نسخ الملخص تلقائياً.', 'error');
      });
    });

    // 1. Detailed Ayah-by-Ayah Analysis & Munasabat
    renderQuranConnections(data.ayahAnalyses, surahNum);

    // 2. Thematics
    renderQuranThematics(data.thematicSections);

    // 3. Mutashabihat
    renderQuranMutashabihat(data.mutashabihat, data.ayahAnalyses);

    // 4. Tadabbur & Vocab
    renderQuranTadabbur(data.coreReflections, data.ayahAnalyses);
  }

  function renderQuranConnections(ayahAnalyses, surahNumber = 1) {
    const list = $('#quran-connections-list');
    const jumpSelect = $('#quran-ayah-jump-select');
    const searchInput = $('#quran-ayah-search');
    const countBadge = $('#ayahs-count-badge');

    if (!list) return;

    if (!ayahAnalyses || ayahAnalyses.length === 0) {
      list.innerHTML = '<p class="empty-msg">لا توجد بيانات تفصيلية متوفرة لهذا المقطع.</p>';
      if (countBadge) countBadge.textContent = '٠ آية';
      return;
    }

    if (countBadge) countBadge.textContent = `${toArabicDigits(ayahAnalyses.length)} آية`;

    // Populate Ayah Jump Select
    if (jumpSelect) {
      jumpSelect.innerHTML = '<option value="all">👁️ عرض جميع الآيات متسلسلة</option>';
      ayahAnalyses.forEach(item => {
        const opt = document.createElement('option');
        opt.value = String(item.ayahNumber);
        opt.textContent = `الآية (${toArabicDigits(item.ayahNumber)})`;
        jumpSelect.appendChild(opt);
      });
    }

    const memorizedSet = getMemorizedAyahs(surahNumber);

    const renderAyahCards = (items) => {
      list.innerHTML = '';
      if (!items || items.length === 0) {
        list.innerHTML = '<p class="empty-msg">لا توجد آيات مطابقة لمعايير البحث أو التصفية.</p>';
        return;
      }

      items.forEach((item) => {
        const card = document.createElement('div');
        card.className = 'ayah-connection-card';
        card.id = `ayah-card-${item.ayahNumber}`;

        const isMem = memorizedSet.has(item.ayahNumber);

        const vocabTagsHtml = (item.vocabulary && item.vocabulary.length > 0)
          ? `
            <div class="ayah-vocab-inline">
              ${item.vocabulary.map(v => `
                <div class="vocab-tag">
                  <span class="vocab-tag-word">﴿ ${escapeHtml(v.word)} ﴾:</span>
                  <span class="vocab-tag-meaning">${escapeHtml(v.meaning)}</span>
                </div>
              `).join('')}
            </div>
          ` : '';

        card.innerHTML = `
          <div class="ayah-header-row">
            <span class="ayah-badge">الآية ${toArabicDigits(item.ayahNumber)}</span>
            ${item.endingFasila ? `<span class="pill-badge pages-badge">الفاصلة: ${escapeHtml(item.endingFasila)}</span>` : ''}
          </div>
          <p class="ayah-quran-text">﴿ ${escapeHtml(item.ayahText)} ﴾</p>

          ${item.generalMeaning ? `
            <div class="ayah-meaning-box">
              <span class="meaning-label">📖 المعنى الإجمالي للآية:</span>
              <p class="meaning-text">${escapeHtml(item.generalMeaning)}</p>
            </div>
          ` : ''}

          ${vocabTagsHtml}

          <div class="ayah-details-grid">
            ${(() => {
              if (!item.contextWithNext) return '';
              let relationTag = '';
              let bodyText = item.contextWithNext.trim();
              const tagMatch = bodyText.match(/^\[(.*?)\]\s*:?\s*/);
              if (tagMatch) {
                relationTag = tagMatch[1];
                bodyText = bodyText.substring(tagMatch[0].length);
              }
              return `
                <div class="ayah-detail-box context-box enhanced-munasaba-box">
                  <div class="munasaba-header-row">
                    <span class="detail-label munasaba-main-label">🔗 وجه المناسبة والربط البياني بالآية التالية:</span>
                    ${relationTag ? `<span class="munasaba-relation-badge">✨ نمط العلاقة: ${escapeHtml(relationTag)}</span>` : ''}
                  </div>
                  <p class="detail-text munasaba-body-text">${escapeHtml(bodyText)}</p>
                </div>
              `;
            })()}
            ${item.endingReason ? `
              <div class="ayah-detail-box fasila-box">
                <span class="detail-label">🎯 سر خاتمة الآية (الفاصلة):</span>
                <p class="detail-text">${escapeHtml(item.endingReason)}</p>
              </div>
            ` : ''}
            ${item.actionableTadabbur ? `
              <div class="ayah-detail-box tadabbur-box">
                <span class="detail-label">✨ الوقفة التدبرية والعملية المستفادة:</span>
                <p class="detail-text">${escapeHtml(item.actionableTadabbur)}</p>
              </div>
            ` : ''}
            ${item.asbabNuzul ? `
              <div class="ayah-detail-box asbab-box">
                <span class="detail-label">📜 سبب النزول المعتمد:</span>
                <p class="detail-text">${escapeHtml(item.asbabNuzul)}</p>
              </div>
            ` : ''}
          </div>

          <div class="ayah-card-actions-row">
            <button type="button" class="ayah-audio-btn" data-ayah-number="${item.ayahNumber}">
              <span>▶️ استمع للآية</span>
            </button>
            <button type="button" class="ayah-memorize-btn ${isMem ? 'is-memorized' : ''}" data-ayah-number="${item.ayahNumber}">
              ${isMem ? '<span>✅ تم الحفظ والإتقان</span>' : '<span>⬜ وضع علامة تم الحفظ</span>'}
            </button>
          </div>
        `;

        // Wire Audio Play Button
        card.querySelector('.ayah-audio-btn')?.addEventListener('click', (e) => {
          const btn = e.currentTarget;
          playAyahAudio(surahNumber, item.ayahNumber, btn);
        });

        // Wire Memorize Toggle Button
        card.querySelector('.ayah-memorize-btn')?.addEventListener('click', (e) => {
          const btn = e.currentTarget;
          const isNowMem = toggleAyahMemorizedState(surahNumber, item.ayahNumber);
          btn.classList.toggle('is-memorized', isNowMem);
          btn.innerHTML = isNowMem ? '<span>✅ تم الحفظ والإتقان</span>' : '<span>⬜ وضع علامة تم الحفظ</span>';
        });

        list.appendChild(card);
      });
    };

    renderAyahCards(ayahAnalyses);

    // Wire Jump Select
    if (jumpSelect) {
      jumpSelect.onchange = (e) => {
        const val = e.target.value;
        if (val === 'all') {
          renderAyahCards(ayahAnalyses);
        } else {
          const num = parseInt(val, 10);
          const filtered = ayahAnalyses.filter(a => a.ayahNumber === num);
          renderAyahCards(filtered);
        }
      };
    }

    // Wire Ayah Search
    if (searchInput) {
      searchInput.value = '';
      searchInput.oninput = (e) => {
        const q = (e.target.value || '').trim().toLowerCase();
        if (!q) {
          if (jumpSelect) jumpSelect.value = 'all';
          renderAyahCards(ayahAnalyses);
        } else {
          const filtered = ayahAnalyses.filter(a =>
            (a.ayahText && a.ayahText.includes(q)) ||
            (a.generalMeaning && a.generalMeaning.includes(q)) ||
            (a.contextWithNext && a.contextWithNext.includes(q)) ||
            (a.endingReason && a.endingReason.includes(q)) ||
            (a.actionableTadabbur && a.actionableTadabbur.includes(q)) ||
            (a.asbabNuzul && a.asbabNuzul.includes(q)) ||
            String(a.ayahNumber).includes(q)
          );
          renderAyahCards(filtered);
        }
      };
    }
  }

  function renderQuranThematics(thematicSections) {
    const grid = $('#quran-thematics-grid');
    if (!grid) return;
    grid.innerHTML = '';

    if (!thematicSections || thematicSections.length === 0) {
      grid.innerHTML = '<p class="empty-msg">لا توجد محاور مسجلة.</p>';
      return;
    }

    thematicSections.forEach((sec) => {
      const card = document.createElement('div');
      card.className = 'thematic-card';
      card.innerHTML = `
        <div>
          <div class="thematic-header">
            <span class="thematic-icon">${sec.icon || '📑'}</span>
            <h4 class="thematic-title">${escapeHtml(sec.title)}</h4>
          </div>
          <span class="thematic-range-badge">الآيات: ${escapeHtml(sec.ayahRange)}</span>
          <p class="thematic-summary">${escapeHtml(sec.summary)}</p>
        </div>
      `;
      grid.appendChild(card);
    });
  }

  function renderQuranMutashabihat(mutashabihat, ayahAnalyses) {
    const tbody = $('#quran-mutashabihat-tbody');
    if (!tbody) return;
    tbody.innerHTML = '';

    if (!mutashabihat || mutashabihat.length === 0) {
      tbody.innerHTML = `
        <tr>
          <td colspan="4" style="text-align: center; padding: 24px; color: var(--ink-muted);">
            ✨ لا توجد مواضع متشابهات مشكلة في هذا المقطع، أو تم ضبطها بوضوح وسلاسة.
          </td>
        </tr>
      `;
      return;
    }

    mutashabihat.forEach((item) => {
      const tr = document.createElement('tr');
      const analyzedAyah = (ayahAnalyses || []).find(ayah => ayah.ayahNumber === item.baseAyahNumber);
      const baseText = analyzedAyah?.ayahText || item.baseAyahText || '';
      tr.innerHTML = `
        <td data-label="الآية في هذا الموضع">
          <strong>آية (${toArabicDigits(item.baseAyahNumber)}):</strong><br/>
          <span class="mutashabihat-ayah-text">﴿ ${escapeHtml(baseText)} ﴾</span>
        </td>
        <td data-label="الموضع المتشابه">
          <strong>${escapeHtml(item.similarSurahOrAyah)}:</strong><br/>
          <span class="mutashabihat-ayah-text similar-ayah-text">﴿ ${escapeHtml(item.similarText)} ﴾</span>
        </td>
        <td data-label="موضع الفرق والتمايز">
          <span class="diff-highlight">${escapeHtml(item.differenceSummary)}</span>
        </td>
        <td data-label="قاعدة وضابط الحفظ الذهني">
          <span class="rule-highlight">${escapeHtml(item.mnemonicRule)}</span>
        </td>
      `;
      tbody.appendChild(tr);
    });
  }

  function renderQuranTadabbur(coreReflections, ayahAnalyses) {
    const reflList = $('#quran-reflections-list');
    if (reflList) {
      reflList.innerHTML = '';
      if (!coreReflections || coreReflections.length === 0) {
        reflList.innerHTML = '<li>تدبر الآيات واستشعر خطاب الله المباشر لقلبك وسلوكك اليومي.</li>';
      } else {
        coreReflections.forEach((ref) => {
          const li = document.createElement('li');
          li.textContent = ref;
          reflList.appendChild(li);
        });
      }
    }

    const vocabGrid = $('#quran-vocabulary-list');
    if (vocabGrid) {
      vocabGrid.innerHTML = '';
      const allVocab = [];
      if (ayahAnalyses) {
        ayahAnalyses.forEach(a => {
          if (a.vocabulary && a.vocabulary.length > 0) {
            a.vocabulary.forEach(v => {
              allVocab.push({
                ayahNumber: a.ayahNumber,
                word: v.word,
                meaning: v.meaning
              });
            });
          }
        });
      }

      const renderVocabCards = (items) => {
        vocabGrid.innerHTML = '';
        if (!items || items.length === 0) {
          vocabGrid.innerHTML = '<p class="empty-msg" style="grid-column: 1/-1;">لا توجد مفردات مطابقة للبحث أو واضحة وميسرة.</p>';
          return;
        }
        items.forEach(v => {
          const card = document.createElement('div');
          card.className = 'vocab-card';
          card.innerHTML = `
            <div>
              <div class="vocab-header">
                <h5 class="vocab-word">﴿ ${escapeHtml(v.word)} ﴾</h5>
                <span class="vocab-ayah-badge">الآية ${toArabicDigits(v.ayahNumber)}</span>
              </div>
              <p class="vocab-meaning">${escapeHtml(v.meaning)}</p>
            </div>
          `;
          vocabGrid.appendChild(card);
        });
      };

      renderVocabCards(allVocab);

      const searchInput = $('#quran-vocab-search');
      if (searchInput) {
        searchInput.value = '';
        searchInput.oninput = (e) => {
          const q = (e.target.value || '').trim().toLowerCase();
          if (!q) {
            renderVocabCards(allVocab);
          } else {
            const filtered = allVocab.filter(v =>
              (v.word && v.word.toLowerCase().includes(q)) ||
              (v.meaning && v.meaning.toLowerCase().includes(q)) ||
              String(v.ayahNumber).includes(q)
            );
            renderVocabCards(filtered);
          }
        };
      }
    }
  }

  // ═══════════════════════════════════════════════════════════
  // INTERACTIVE REVIEWS & CONSOLIDATION WORKSPACE ENGINE
  // ═══════════════════════════════════════════════════════════

  function initReviewWorkspace() {
    // 1. Subtabs switching
    $$('.review-tab-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.reviewTab;
        switchReviewTab(tab);
      });
    });

    // 2. Quick action buttons in header
    $('#start-auto-reinforce-btn')?.addEventListener('click', openAutoReinforcement);
    $('#quick-start-flashcards-btn')?.addEventListener('click', () => switchReviewTab('flashcards'));
    $('#quick-start-quiz-btn')?.addEventListener('click', () => switchReviewTab('quiz'));

    // 3. Flashcard controls
    const flashcardEl = $('#flashcard-card');
    flashcardEl?.addEventListener('click', flipFlashcard);
    flashcardEl?.addEventListener('keydown', (e) => {
      if (e.key === ' ' || e.key === 'Enter') {
        e.preventDefault();
        flipFlashcard();
      }
    });

    $('#flip-card-btn')?.addEventListener('click', flipFlashcard);
    $('#prev-flashcard-btn')?.addEventListener('click', () => {
      if (state.currentFlashcardIndex > 0) {
        state.currentFlashcardIndex--;
        renderCurrentFlashcard();
      }
    });
    $('#next-flashcard-btn')?.addEventListener('click', () => {
      if (state.currentFlashcardIndex < state.flashcards.length - 1) {
        state.currentFlashcardIndex++;
        renderCurrentFlashcard();
      }
    });
    $('#shuffle-flashcards-btn')?.addEventListener('click', () => {
      shuffleArray(state.flashcards);
      state.currentFlashcardIndex = 0;
      renderCurrentFlashcard();
      showToast('تم خلط البطاقات عشوائياً 🔀', 'info');
    });
    $('#reset-flashcards-btn')?.addEventListener('click', () => {
      state.currentFlashcardIndex = 0;
      renderCurrentFlashcard();
    });
    $('#restart-deck-btn')?.addEventListener('click', () => {
      state.currentFlashcardIndex = 0;
      $('#flashcards-finished-card').hidden = true;
      $('#flashcard-scene').hidden = false;
      $('#flashcard-rating-bar').hidden = false;
      renderCurrentFlashcard();
    });
    $('#goto-matrix-btn')?.addEventListener('click', () => switchReviewTab('matrix'));

    // Rating buttons
    $$('.rating-btn').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const rating = btn.dataset.rating;
        rateActiveFlashcard(rating);
      });
    });

    // 4. Quiz controls
    $('#refresh-quiz-btn')?.addEventListener('click', () => loadReviewQuiz(true));

    // 5. Matrix search & filters
    $('#matrix-search-input')?.addEventListener('input', (e) => {
      state.matrixSearchQuery = (e.target.value || '').trim().toLowerCase();
      filterAndRenderMatrix();
    });

    $$('.filter-pill').forEach((pill) => {
      pill.addEventListener('click', () => {
        $$('.filter-pill').forEach(p => p.classList.toggle('active', p === pill));
        state.matrixFilter = pill.dataset.matrixFilter;
        filterAndRenderMatrix();
      });
    });

    // 6. Reinforcement Modal controls
    $('#close-reinforce-modal-btn')?.addEventListener('click', closeSmartReinforcementModal);
    $('#reinforcement-modal')?.addEventListener('click', (e) => {
      if (e.target.id === 'reinforcement-modal') {
        closeSmartReinforcementModal();
      }
    });
    $('#reinforce-answer-form')?.addEventListener('submit', handleReinforceAnswerSubmit);
    $('#modal-done-btn')?.addEventListener('click', () => {
      closeSmartReinforcementModal();
      refreshWorkspace();
      refreshReviewWorkspace();
    });
  }

  function switchReviewTab(tabName) {
    state.activeReviewTab = tabName;

    let activeBtn = null;
    $$('.review-tab-btn').forEach((btn) => {
      const isActive = btn.dataset.reviewTab === tabName;
      btn.classList.toggle('active', isActive);
      btn.setAttribute('aria-selected', String(isActive));
      if (isActive) activeBtn = btn;
    });

    if (activeBtn && typeof activeBtn.scrollIntoView === 'function') {
      activeBtn.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
    }

    const panes = {
      'plan': $('#review-tab-plan'),
      'flashcards': $('#review-tab-flashcards'),
      'quiz': $('#review-tab-quiz'),
      'matrix': $('#review-tab-matrix')
    };

    Object.entries(panes).forEach(([name, pane]) => {
      if (pane) {
        const isActive = name === tabName;
        pane.hidden = !isActive;
        pane.classList.toggle('active', isActive);
      }
    });

    if (tabName === 'flashcards' && state.flashcards.length === 0) {
      loadFlashcardsDeck();
    } else if (tabName === 'quiz' && !state.quickQuiz) {
      loadReviewQuiz();
    } else if (tabName === 'matrix' && state.masteryMatrix.length === 0) {
      loadMasteryMatrix();
    }
  }

  async function refreshReviewWorkspace() {
    if (!state.activeBook) return;

    if (state.activeReviewTab === 'flashcards') {
      await loadFlashcardsDeck();
    } else if (state.activeReviewTab === 'quiz') {
      await loadReviewQuiz();
    } else if (state.activeReviewTab === 'matrix') {
      await loadMasteryMatrix();
    }
  }

  function shuffleArray(arr) {
    for (let i = arr.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [arr[i], arr[j]] = [arr[j], arr[i]];
    }
  }

  // ── Auto Reinforcement Trigger ─────────────────────────────
  function openAutoReinforcement() {
    const weak = state.weakConcepts?.[0];
    const due = state.dueReviews?.[0];

    const target = weak || due;
    if (target) {
      const conceptKey = target.conceptKey;
      const lessonId = target.lastAssessedLessonId || target.firstIntroducedLessonId || target.lessonId;
      openSmartReinforcement(conceptKey, lessonId);
    } else if (state.masteryMatrix.length > 0) {
      const first = state.masteryMatrix[0];
      openSmartReinforcement(first.conceptKey, first.lessonId);
    } else {
      showToast('لا توجد مفاهيم مسجلة في هذا الكتاب بعد. أضف صفحات لبدء المدارسة.', 'info');
    }
  }

  // ── AI Reinforcement Modal Implementation ───────────────────
  async function openSmartReinforcement(conceptKey, lessonId = null) {
    if (!state.activeBook || !conceptKey) return;

    const modal = $('#reinforcement-modal');
    const loading = $('#reinforce-modal-loading');
    const content = $('#reinforce-content-view');
    const feedbackBox = $('#modal-evaluation-feedback');
    const form = $('#reinforce-answer-form');
    const input = $('#modal-answer-input');

    if (!modal) return;

    modal.hidden = false;
    loading.hidden = false;
    content.hidden = true;
    feedbackBox.hidden = true;
    form.hidden = false;
    input.value = '';
    input.disabled = false;

    $('#modal-concept-title').textContent = conceptKey;

    try {
      const session = await api(`/learning/${state.activeBook.id}/reinforce-concept`, {
        method: 'POST',
        body: JSON.stringify({
          conceptKey: conceptKey,
          lessonId: lessonId || null
        })
      });

      state.activeReinforceSession = session;
      renderReinforcementSession(session);
    } catch (err) {
      showToast(err.message, 'error');
      closeSmartReinforcementModal();
    }
  }

  function renderReinforcementSession(session) {
    const loading = $('#reinforce-modal-loading');
    const content = $('#reinforce-content-view');

    loading.hidden = true;
    content.hidden = false;

    $('#modal-concept-title').textContent = session.conceptTitle || session.conceptKey;
    $('#modal-explanation-text').textContent = session.simplifiedExplanation || 'سيظهر الشرح الميسر هنا.';

    const evidenceCard = $('#modal-evidence-card');
    if (session.keyEvidenceText && session.keyEvidenceText.trim()) {
      $('#modal-evidence-text').textContent = `« ${session.keyEvidenceText} »`;
      $('#modal-evidence-role-title').textContent = session.keyEvidenceRole ? `الدليل والاستدلال (${session.keyEvidenceRole}):` : 'الدليل ووجه الاستدلال:';
      evidenceCard.hidden = false;
    } else {
      evidenceCard.hidden = true;
    }

    $('#modal-misconception-text').textContent = session.coreMisconceptionClarification || 'الخلط الشائع يقع في عدم ضبط وجه الاستدلال الدقيق أو مسلك الجمع بين الأدلة.';
    $('#modal-mnemonic-text').textContent = session.mnemonicOrMemoryAnchor || '💡 اربط المسألة بحكمها واستحضر لفظ الحديث المعتمد.';

    $('#modal-question-prompt').textContent = session.verificationQuestion || `اشرح مفهوم (${session.conceptKey}) وبيّن كيفية الاستدلال عليه.`;
    $('#modal-question-guidance').textContent = session.questionGuidance || 'أجب بأسلوبك مع التركيز على وجه الاستدلال.';
    $('#modal-question-type-badge').textContent = session.questionType === 'Understanding' ? 'تحقق واستيعاب' : 'فهم واستدلال';
  }

  async function handleReinforceAnswerSubmit(e) {
    e.preventDefault();

    const input = $('#modal-answer-input');
    const answer = input.value.trim();
    if (!answer) {
      showToast('يرجى كتابة إجابتك أولاً.', 'warning');
      return;
    }

    const session = state.activeReinforceSession;
    if (!session || !state.activeBook) return;

    const btn = $('#modal-submit-answer-btn');
    btn.disabled = true;
    btn.querySelector('.spinner').hidden = false;
    input.disabled = true;

    try {
      const result = await api(`/learning/${state.activeBook.id}/evaluate-reinforcement`, {
        method: 'POST',
        body: JSON.stringify({
          conceptKey: session.conceptKey,
          studentAnswer: answer,
          lessonId: session.sourceLessonId || null
        })
      });

      renderReinforcementFeedback(result);
    } catch (err) {
      showToast(err.message, 'error');
      btn.disabled = false;
      btn.querySelector('.spinner').hidden = true;
      input.disabled = false;
    }
  }

  function renderReinforcementFeedback(result) {
    const form = $('#reinforce-answer-form');
    const feedbackBox = $('#modal-evaluation-feedback');

    form.hidden = true;
    feedbackBox.hidden = false;

    const statusPill = $('#modal-feedback-status-pill');
    statusPill.className = 'status-pill';

    if (result.isMasteredOrUnderstood) {
      statusPill.textContent = 'تم التثبيت بنجاح ✓';
      statusPill.classList.add('correct');
    } else {
      statusPill.textContent = 'إجابة جيدة — تحتاج لمزيد من الضبط';
      statusPill.classList.add('partial');
    }

    const pct = Math.round(result.score * 100);
    $('#modal-feedback-score-pill').textContent = `الدرجة: ${toArabicDigits(pct)}٪ — المستوى الجديد: ${getLevelArabicName(result.NewLevel || result.newLevel)}`;
    $('#modal-feedback-comment').textContent = result.feedback || 'أحسنت في بيان وجه المسألة.';

    const understoodGroup = $('#modal-understood-points-group');
    const understoodList = $('#modal-understood-points-list');
    const understood = result.UnderstoodPoints || result.understoodPoints || [];
    if (understood.length > 0) {
      understoodList.innerHTML = understood.map(p => `<span class="concept-tag">✓ ${escapeHtml(p)}</span>`).join('');
      understoodGroup.hidden = false;
    } else {
      understoodGroup.hidden = true;
    }

    const adviceGroup = $('#modal-advice-points-group');
    const adviceList = $('#modal-advice-points-list');
    const advice = result.AdvicePoints || result.advicePoints || [];
    if (advice.length > 0) {
      adviceList.innerHTML = advice.map(p => `<span class="concept-tag">💡 ${escapeHtml(p)}</span>`).join('');
      adviceGroup.hidden = false;
    } else {
      adviceGroup.hidden = true;
    }

    feedbackBox.scrollIntoView({ behavior: 'smooth' });
    showToast('تم تحديث إتقان المفهوم في الذاكرة التراكمية بنجاح ✓', 'success');
  }

  function closeSmartReinforcementModal() {
    const modal = $('#reinforcement-modal');
    if (modal) modal.hidden = true;
    state.activeReinforceSession = null;
  }

  // ── Flashcards Spaced Repetition Engine ───────────────────────
  async function loadFlashcardsDeck() {
    if (!state.activeBook) return;

    try {
      const deck = await api(`/learning/${state.activeBook.id}/review-deck`);
      state.flashcards = deck || [];
      state.currentFlashcardIndex = 0;

      $('#flashcards-finished-card').hidden = true;
      $('#flashcard-scene').hidden = false;
      $('#flashcard-rating-bar').hidden = false;

      renderCurrentFlashcard();
    } catch (err) {
      console.error('Failed to load flashcard deck:', err);
    }
  }

  function renderCurrentFlashcard() {
    const deck = state.flashcards;
    const scene = $('#flashcard-scene');
    const finishedCard = $('#flashcards-finished-card');
    const ratingBar = $('#flashcard-rating-bar');

    if (!deck || deck.length === 0) {
      if (scene) scene.hidden = true;
      if (ratingBar) ratingBar.hidden = true;
      if (finishedCard) {
        finishedCard.hidden = false;
        finishedCard.innerHTML = `
          <div class="finished-icon">🗂️</div>
          <h3>لا توجد بطاقات متاحة حالياً</h3>
          <p>أضف مقاطع أو دروساً للكتاب ليتم إنشاء بطاقات الاسترجاع الذكية تلقائياً.</p>
        `;
      }
      return;
    }

    const idx = state.currentFlashcardIndex;
    if (idx >= deck.length) {
      scene.hidden = true;
      ratingBar.hidden = true;
      finishedCard.hidden = false;
      return;
    }

    scene.hidden = false;
    ratingBar.hidden = false;
    finishedCard.hidden = true;

    const card = deck[idx];
    const total = deck.length;

    // Reset 3D flip rotation
    $('#flashcard-card')?.classList.remove('is-flipped');

    // Counter & progress
    $('#flashcard-counter').textContent = `بطاقة ${toArabicDigits(idx + 1)} من ${toArabicDigits(total)}`;
    $('#flashcard-category-badge').textContent = card.category || 'مسألة فقهية';

    const progressPct = Math.round(((idx) / total) * 100);
    $('#flashcards-progress-fill').style.width = `${progressPct}%`;

    // Front content
    $('#flashcard-front-title').textContent = card.frontText;
    $('#flashcard-front-subtitle').textContent = card.frontSubtitle || '';
    $('#flashcard-front-lesson').textContent = card.lessonTitle || state.activeBook?.title || 'كتاب دراسي';

    // Back content
    $('#flashcard-back-title').textContent = card.backTitle || 'الحكم والضابط:';
    $('#flashcard-back-explanation').textContent = card.backExplanation || '';

    const evBox = $('#flashcard-evidence-box');
    if (card.evidenceSnippet && card.evidenceSnippet.trim()) {
      $('#flashcard-evidence-text').textContent = card.evidenceSnippet;
      evBox.hidden = false;
    } else {
      evBox.hidden = true;
    }

    const memBox = $('#flashcard-memory-box');
    if (card.memoryTip && card.memoryTip.trim()) {
      $('#flashcard-memory-text').textContent = card.memoryTip;
      memBox.hidden = false;
    } else {
      memBox.hidden = true;
    }

    $('#flashcard-back-level').textContent = `المستوى: ${getLevelArabicName(card.currentLevel || 'Familiar')}`;

    // Nav buttons state
    const prevBtn = $('#prev-flashcard-btn');
    const nextBtn = $('#next-flashcard-btn');
    if (prevBtn) prevBtn.disabled = idx === 0;
    if (nextBtn) nextBtn.disabled = idx === total - 1;
  }

  function flipFlashcard() {
    const cardEl = $('#flashcard-card');
    if (cardEl) {
      cardEl.classList.toggle('is-flipped');
    }
  }

  async function rateActiveFlashcard(rating) {
    const idx = state.currentFlashcardIndex;
    const card = state.flashcards[idx];
    if (!card || !state.activeBook) return;

    try {
      await api(`/learning/${state.activeBook.id}/rate-flashcard`, {
        method: 'POST',
        body: JSON.stringify({
          conceptKey: card.conceptKey,
          rating: rating
        })
      });

      if (rating === 'Hard') {
        // Repeat card later in this deck session
        state.flashcards.push({ ...card });
      }

      state.currentFlashcardIndex++;
      renderCurrentFlashcard();
    } catch (err) {
      console.error('Rate flashcard error:', err);
      state.currentFlashcardIndex++;
      renderCurrentFlashcard();
    }
  }

  // ── Quick Review Quiz Engine ────────────────────────────────
  async function loadReviewQuiz(forceRefresh = false) {
    if (!state.activeBook) return;

    const loadingState = $('#quiz-loading-state');
    const questionsList = $('#quiz-questions-list');

    if (loadingState) loadingState.hidden = false;
    if (questionsList) questionsList.hidden = true;

    try {
      const quiz = await api(`/learning/${state.activeBook.id}/quick-review-quiz?count=3`);
      state.quickQuiz = quiz;
      state.quickQuizAnswers = new Map();
      renderReviewQuiz(quiz);
    } catch (err) {
      if (loadingState) loadingState.hidden = true;
      if (questionsList) {
        questionsList.hidden = false;
        questionsList.innerHTML = `<div class="empty-state">${escapeHtml(err.message)}</div>`;
      }
    }
  }

  function renderReviewQuiz(quiz) {
    const loadingState = $('#quiz-loading-state');
    const questionsList = $('#quiz-questions-list');

    if (loadingState) loadingState.hidden = true;
    if (questionsList) questionsList.hidden = false;

    if (!quiz || !quiz.questions || quiz.questions.length === 0) {
      questionsList.innerHTML = '<div class="empty-state">لا توجد أسئلة مراجعة حالية. أضف مقاطع للكتاب أولاً.</div>';
      return;
    }

    questionsList.innerHTML = quiz.questions.map((q, idx) => `
      <div class="quiz-question-card" id="quiz-card-${idx}">
        <div class="quiz-question-header">
          <span class="pill-badge">سؤال ${toArabicDigits(idx + 1)} من ${toArabicDigits(quiz.questions.length)}</span>
          <span class="pill-badge muted-badge">${escapeHtml(q.conceptKey || 'مسألة فقهية')}</span>
        </div>
        <h4 class="quiz-question-text">${escapeHtml(q.question)}</h4>
        <p class="quiz-question-hint">${escapeHtml(q.guidance || 'أجب بأسلوبك مع ذكر وجه الاستدلال.')}</p>

        <form class="quiz-form" data-quiz-index="${idx}">
          <textarea class="answer-textarea quiz-answer-input" rows="3" placeholder="اكتب إجابتك هنا..." required></textarea>
          <div class="form-bottom-row mt-2">
            <button type="submit" class="primary-btn small-btn submit-quiz-btn">
              <span class="btn-text">تقييم الإجابة</span>
              <span class="spinner" hidden aria-hidden="true"></span>
            </button>
          </div>
        </form>

        <div class="quiz-feedback-box mt-3" id="quiz-feedback-${idx}" hidden></div>
      </div>
    `).join('');

    quiz.questions.forEach((q, idx) => {
      const card = $(`#quiz-card-${idx}`);
      const form = card?.querySelector('.quiz-form');
      form?.addEventListener('submit', (e) => handleQuizSubmit(idx, q, e));
    });
  }

  async function handleQuizSubmit(idx, question, e) {
    e.preventDefault();

    const card = $(`#quiz-card-${idx}`);
    const input = card.querySelector('.quiz-answer-input');
    const submitBtn = card.querySelector('.submit-quiz-btn');
    const feedbackBox = $(`#quiz-feedback-${idx}`);
    const answer = input.value.trim();

    if (!answer || !state.activeBook) return;

    input.disabled = true;
    submitBtn.disabled = true;
    submitBtn.querySelector('.btn-text').textContent = 'جاري التقييم...';
    submitBtn.querySelector('.spinner').hidden = false;

    try {
      const result = await api(`/learning/${state.activeBook.id}/evaluate-reinforcement`, {
        method: 'POST',
        body: JSON.stringify({
          conceptKey: question.conceptKey,
          studentAnswer: answer,
          lessonId: question.sourceLessonId || null
        })
      });

      submitBtn.hidden = true;
      feedbackBox.hidden = false;

      const pct = Math.round(result.score * 100);
      const isCorrect = result.isMasteredOrUnderstood;

      feedbackBox.innerHTML = `
        <div class="modal-feedback-box">
          <div class="feedback-status-row">
            <span class="status-pill ${isCorrect ? 'correct' : 'partial'}">${isCorrect ? 'إجابة ممتازة ومستوعبة ✓' : 'تحتاج لمزيد من الضبط'}</span>
            <span class="level-pill">الدرجة: ${toArabicDigits(pct)}٪</span>
          </div>
          <p class="feedback-text mt-2">${escapeHtml(result.feedback || '')}</p>
        </div>
      `;

      showToast(`تم تقييم السؤال (${toArabicDigits(idx + 1)}) بنجاح ✓`, 'success');
    } catch (err) {
      input.disabled = false;
      submitBtn.disabled = false;
      submitBtn.querySelector('.btn-text').textContent = 'إعادة الإرسال';
      submitBtn.querySelector('.spinner').hidden = true;
      showToast(err.message, 'error');
    }
  }

  // ── Mastery Matrix & Concept Map Engine ───────────────────────
  async function loadMasteryMatrix() {
    if (!state.activeBook) return;

    try {
      const matrix = await api(`/learning/${state.activeBook.id}/mastery-matrix`);
      state.masteryMatrix = matrix || [];
      filterAndRenderMatrix();
    } catch (err) {
      console.error('Failed to load mastery matrix:', err);
    }
  }

  function filterAndRenderMatrix() {
    const list = state.masteryMatrix;
    const filter = state.matrixFilter || 'all';
    const query = state.matrixSearchQuery || '';

    // Update pill counters
    const countAll = list.length;
    const countWeak = list.filter(m => m.isWeak).length;
    const countDue = list.filter(m => m.isReviewDue).length;
    const countMastered = list.filter(m => m.learningLevel === 3 || m.learningLevel === 'Mastered').length;
    const countUnderstood = list.filter(m => m.learningLevel === 2 || m.learningLevel === 'Understood').length;
    const countFamiliar = list.filter(m => m.learningLevel === 1 || m.learningLevel === 'Familiar').length;

    if ($('#count-all')) $('#count-all').textContent = toArabicDigits(countAll);
    if ($('#count-weak')) $('#count-weak').textContent = toArabicDigits(countWeak);
    if ($('#count-due')) $('#count-due').textContent = toArabicDigits(countDue);
    if ($('#count-mastered')) $('#count-mastered').textContent = toArabicDigits(countMastered);
    if ($('#count-understood')) $('#count-understood').textContent = toArabicDigits(countUnderstood);
    if ($('#count-familiar')) $('#count-familiar').textContent = toArabicDigits(countFamiliar);

    let filtered = list;

    if (filter === 'weak') filtered = filtered.filter(m => m.isWeak);
    else if (filter === 'due') filtered = filtered.filter(m => m.isReviewDue);
    else if (filter === 'mastered') filtered = filtered.filter(m => m.learningLevel === 3 || m.learningLevel === 'Mastered');
    else if (filter === 'understood') filtered = filtered.filter(m => m.learningLevel === 2 || m.learningLevel === 'Understood');
    else if (filter === 'familiar') filtered = filtered.filter(m => m.learningLevel === 1 || m.learningLevel === 'Familiar');

    if (query) {
      filtered = filtered.filter(m =>
        (m.conceptKey && m.conceptKey.toLowerCase().includes(query)) ||
        (m.lessonTitle && m.lessonTitle.toLowerCase().includes(query)) ||
        (m.statusDescription && m.statusDescription.toLowerCase().includes(query))
      );
    }

    renderMatrixGrid(filtered);
  }

  function renderMatrixGrid(items) {
    const grid = $('#matrix-grid');
    if (!grid) return;

    if (!items || items.length === 0) {
      grid.innerHTML = '<div class="empty-state" style="grid-column: 1/-1;">لا توجد مفاهيم مطابقة للتصنيف أو البحث الحالي.</div>';
      return;
    }

    grid.innerHTML = items.map((m) => {
      const confidence = typeof m.confidence === 'number' ? Math.round(m.confidence * 100) : 50;
      const levelName = getLevelArabicName(m.learningLevel);
      const isWeak = m.isWeak;
      const isDue = m.isReviewDue;

      return `
        <div class="matrix-card">
          <div>
            <div class="matrix-card-header">
              <h4 class="matrix-concept-name">${escapeHtml(m.conceptKey)}</h4>
              <span class="pill-badge ${isWeak ? 'insufficient' : isDue ? 'pages-badge' : 'muted-badge'}">${levelName}</span>
            </div>
            <p class="matrix-concept-desc">${escapeHtml(m.statusDescription || 'مفهوم مسجل في الذاكرة التراكمية.')}</p>
            
            <div class="matrix-card-stats">
              <div class="matrix-stat-row">
                <span>نسبة الثقة والاستقرار:</span>
                <strong>${toArabicDigits(confidence)}٪</strong>
              </div>
              <div class="matrix-stat-row">
                <span>مرات التعرض والتقييم:</span>
                <span>${toArabicDigits(m.exposureCount || 1)} تعرض | ${toArabicDigits(m.assessmentCount || 0)} تقييم</span>
              </div>
              ${m.lessonTitle ? `
                <div class="matrix-stat-row">
                  <span>الدرس المرتبط:</span>
                  <span class="text-truncate" style="max-width: 160px;">${escapeHtml(m.lessonTitle)}</span>
                </div>
              ` : ''}
            </div>
          </div>

          <div class="matrix-card-actions">
            <button type="button" class="primary-btn small-btn matrix-reinforce-btn" data-concept-key="${escapeHtml(m.conceptKey)}" data-lesson-id="${m.lessonId || ''}">
              <span>🎯 تثبيت ذكي</span>
            </button>
            ${m.lessonId ? `
              <button type="button" class="subtle-btn small-btn matrix-open-lesson-btn" data-lesson-id="${m.lessonId}">
                <span>📖 فتح الدرس</span>
              </button>
            ` : ''}
          </div>
        </div>
      `;
    }).join('');

    $$('.matrix-reinforce-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        openSmartReinforcement(btn.dataset.conceptKey, btn.dataset.lessonId);
      });
    });

    $$('.matrix-open-lesson-btn').forEach((btn) => {
      btn.addEventListener('click', () => {
        const id = btn.dataset.lessonId;
        if (id) openLesson(id);
      });
    });
  }

  // ==========================================================================
  // Lesson AI Scholarly Tutor Chatbot & Persistent Sessions Manager
  // ==========================================================================

  function initLessonChatbot() {
    // Floating open button
    $('#lesson-chat-floating-btn')?.addEventListener('click', () => {
      toggleLessonChat();
    });

    // In-page inquiry triggers
    $('#lessons-open-chat-btn')?.addEventListener('click', () => {
      openLessonChat(null);
    });

    $('#reader-open-chat-btn')?.addEventListener('click', () => {
      openLessonChat(state.currentLesson?.id || null);
    });

    // Header action buttons
    $('#lesson-chat-expand-btn')?.addEventListener('click', () => {
      toggleFullscreenChat();
    });

    $('#lesson-chat-close-btn')?.addEventListener('click', () => {
      closeLessonChat();
    });

    $('#lesson-chat-minimize-btn')?.addEventListener('click', () => {
      closeLessonChat();
    });

    $('#lesson-chat-clear-btn')?.addEventListener('click', () => {
      clearChatHistory();
    });

    // Session controls
    $('#chat-session-select')?.addEventListener('change', (e) => {
      const selectedSessionId = e.target.value;
      if (selectedSessionId === '__new_session__') {
        createNewChatSession();
      } else {
        loadChatSessionMessages(selectedSessionId);
      }
    });

    $('#chat-new-session-btn')?.addEventListener('click', () => {
      createNewChatSession();
    });

    $('#chat-delete-session-btn')?.addEventListener('click', () => {
      deleteActiveChatSession();
    });

    // Escape key listener for fullscreen/chat close
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && state.isChatOpen) {
        const drawer = $('#lesson-chat-drawer');
        if (drawer && drawer.classList.contains('is-fullscreen')) {
          toggleFullscreenChat();
        } else {
          closeLessonChat();
        }
      }
    });

    // Chat form submit
    $('#lesson-chat-form')?.addEventListener('submit', (e) => {
      e.preventDefault();
      const input = $('#lesson-chat-input');
      const msg = (input?.value || '').trim();
      if (!msg) return;
      input.value = '';
      sendChatMessage(msg);
    });

    // Enter to submit / Shift+Enter for newline
    $('#lesson-chat-input')?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        $('#lesson-chat-form')?.requestSubmit();
      }
    });

    // Ensure typing indicator is hidden on init
    const typing = $('#lesson-chat-typing');
    if (typing) typing.hidden = true;

    // Quick suggestion chips
    wireSuggestionChips();
  }

  function toggleFullscreenChat() {
    const drawer = $('#lesson-chat-drawer');
    const expandBtn = $('#lesson-chat-expand-btn');
    if (!drawer) return;

    const isFullscreen = drawer.classList.toggle('is-fullscreen');
    if (expandBtn) {
      expandBtn.textContent = isFullscreen ? '🗗' : '⛶';
      expandBtn.title = isFullscreen ? 'استعادة الحجم الأصلي' : 'تكبير لملء الشاشة';
      expandBtn.setAttribute('aria-label', isFullscreen ? 'استعادة الحجم الأصلي' : 'تكبير لملء الشاشة');
    }
  }

  function wireSuggestionChips() {
    $$('.chat-suggest-chip').forEach((btn) => {
      btn.onclick = () => {
        const q = btn.dataset.question || btn.textContent.trim();
        if (q) {
          if (!state.isChatOpen) openLessonChat(state.currentLesson?.id || null);
          sendChatMessage(q);
        }
      };
    });
  }

  function toggleLessonChat() {
    if (state.isChatOpen) {
      closeLessonChat();
    } else {
      openLessonChat(state.currentLesson?.id || null);
    }
  }

  async function openLessonChat(lessonId = null) {
    state.isChatOpen = true;
    state.chatContextLessonId = lessonId || state.currentLesson?.id || null;

    const drawer = $('#lesson-chat-drawer');
    if (drawer) {
      drawer.hidden = false;
    }

    const floatingBtn = $('#lesson-chat-floating-btn');
    if (floatingBtn) {
      floatingBtn.style.display = 'none';
    }

    updateChatContextDisplay();

    // Load available chat sessions from backend database
    await loadChatSessions();

    // Focus input
    setTimeout(() => {
      $('#lesson-chat-input')?.focus();
    }, 150);
  }

  function closeLessonChat() {
    state.isChatOpen = false;
    const drawer = $('#lesson-chat-drawer');
    if (drawer) {
      drawer.hidden = true;
    }

    const floatingBtn = $('#lesson-chat-floating-btn');
    if (floatingBtn) {
      floatingBtn.style.display = 'inline-flex';
    }
  }

  function updateChatContextDisplay(customSummary = null) {
    const pill = $('#lesson-chat-context-pill');
    if (!pill) return;

    if (customSummary) {
      pill.textContent = customSummary;
      pill.title = customSummary;
      return;
    }

    if (state.currentLesson && state.activeView === 'reader') {
      pill.textContent = `مدارسة: ${state.currentLesson.title}`;
      pill.title = state.currentLesson.title;
    } else if (state.activeBook) {
      pill.textContent = `كتاب: ${state.activeBook.title}`;
      pill.title = state.activeBook.title;
    } else {
      pill.textContent = 'سياق: عام للمقرر';
      pill.title = 'سياق عام للمقرر';
    }
  }

  async function loadChatSessions(preferSessionId = null) {
    try {
      const bookId = state.activeBook?.id || '';
      const lessonId = state.chatContextLessonId || '';
      let url = '/lessons/chat/sessions';
      const params = [];
      if (lessonId) params.push(`lessonId=${lessonId}`);
      else if (bookId) params.push(`bookId=${bookId}`);
      if (params.length > 0) url += `?${params.join('&')}`;

      const sessions = await api(url).catch(() => []);
      state.chatSessions = sessions || [];

      renderChatSessionsSelect(preferSessionId);
    } catch (err) {
      console.warn('Failed to load chat sessions:', err);
    }
  }

  function renderChatSessionsSelect(preferSessionId = null) {
    const select = $('#chat-session-select');
    if (!select) return;

    select.innerHTML = '<option value="__new_session__">➕ محادثة جديدة...</option>';

    state.chatSessions.forEach((s) => {
      const opt = document.createElement('option');
      opt.value = s.id;
      const countText = s.messageCount > 0 ? ` (${toArabicDigits(s.messageCount)} رسائل)` : '';
      opt.textContent = `💬 ${s.title}${countText}`;
      select.appendChild(opt);
    });

    const targetId = preferSessionId || state.chatSessionId;
    if (targetId && state.chatSessions.some((s) => s.id === targetId)) {
      select.value = targetId;
      if (!preferSessionId && (!state.chatMessages || state.chatMessages.length === 0)) {
        loadChatSessionMessages(targetId);
      }
    } else if (state.chatSessions.length > 0 && !state.chatSessionId) {
      select.value = state.chatSessions[0].id;
      loadChatSessionMessages(state.chatSessions[0].id);
    } else {
      select.value = '__new_session__';
      if (!state.chatMessages || state.chatMessages.length === 0) {
        renderDefaultWelcomeMessage();
      }
    }
  }

  async function loadChatSessionMessages(sessionId) {
    if (!sessionId || sessionId === '__new_session__') {
      createNewChatSession();
      return;
    }

    try {
      state.chatSessionId = sessionId;
      const session = await api(`/lessons/chat/sessions/${sessionId}`);
      if (!session) return;

      state.chatMessages = session.messages || [];

      const container = $('#lesson-chat-messages');
      if (!container) return;
      container.innerHTML = '';

      if (state.chatMessages.length === 0) {
        renderDefaultWelcomeMessage();
      } else {
        state.chatMessages.forEach((msg) => {
          appendChatMessageElement(msg);
        });

        // Update suggested prompts from last assistant message if present
        const lastTutorMsg = [...state.chatMessages].reverse().find((m) => m.role === 'assistant');
        if (lastTutorMsg?.suggestedQuestions && lastTutorMsg.suggestedQuestions.length > 0) {
          updateSuggestedPrompts(lastTutorMsg.suggestedQuestions);
        }
      }

      if (session.contextSummary) {
        updateChatContextDisplay(session.contextSummary);
      } else {
        updateChatContextDisplay();
      }

      scrollChatToBottom();
    } catch (err) {
      showToast(`تعذر استرجاع رسائل الجلسة: ${err.message}`, 'error');
    }
  }

  function createNewChatSession() {
    state.chatSessionId = null;
    state.chatMessages = [];
    const select = $('#chat-session-select');
    if (select) select.value = '__new_session__';
    renderDefaultWelcomeMessage();
    updateChatContextDisplay();
  }

  async function deleteActiveChatSession() {
    if (!state.chatSessionId) {
      showToast('أنت حالياً في محادثة جديدة غير محفوظة.', 'info');
      return;
    }

    if (!confirm('هل أنت متأكد من رغبتك في حذف هذه المحادثة بالكامل من السجل؟')) {
      return;
    }

    try {
      await api(`/lessons/chat/sessions/${state.chatSessionId}`, { method: 'DELETE' });
      showToast('تم حذف المحادثة من السجل بنجاح ✓', 'success');
      state.chatSessionId = null;
      await loadChatSessions();
    } catch (err) {
      showToast(`فشل حذف المحادثة: ${err.message}`, 'error');
    }
  }

  async function clearChatHistory() {
    if (state.chatSessionId) {
      try {
        await api(`/lessons/chat/history?sessionId=${state.chatSessionId}`, { method: 'DELETE' });
        showToast('تم مسح رسائل الجلسة الحالية ✓', 'success');
      } catch (err) {
        console.warn('Could not clear history on backend:', err);
      }
    }

    state.chatMessages = [];
    renderDefaultWelcomeMessage();
  }

  function renderDefaultWelcomeMessage() {
    const container = $('#lesson-chat-messages');
    if (!container) return;

    container.innerHTML = `
      <div class="chat-message-item tutor-message">
        <div class="message-avatar">🧠</div>
        <div class="message-bubble">
          <div class="message-header"><strong>المعلم الذكي</strong> <span>الآن</span></div>
          <div class="message-text">
            <p>مرحباً بك يا طالب العلم! أنا رفيقك ومعلمك الذكي في مدارسة نصوص ومسائل هذا الكتاب. يمكنك سؤالي عن أي غريب لفظ، أو وجه استدلال، أو حال راوٍ، أو استيضاح حكم فقهي.</p>
          </div>
        </div>
      </div>
    `;

    updateSuggestedPrompts([
      'اشرح لي وجه الاستدلال بالحديث في هذا الباب بأسلوب مبسط',
      'ما هي الفوائد التربوية والإيمانية المستفادة من هذا الدرس؟',
      'من هم الرواة المذكورون في هذا الإسناد وما رتبتهم وحالهم؟',
      'ما هي القواعد والضوابط الفقهية المستخلصة من هذا المقطع؟'
    ]);
  }

  async function sendChatMessage(userText) {
    if (!userText || state.isChatLoading) return;

    // Append user message
    const userMsg = { role: 'user', content: userText };
    state.chatMessages.push(userMsg);
    appendChatMessageElement(userMsg);

    state.isChatLoading = true;
    const typingIndicator = $('#lesson-chat-typing');
    if (typingIndicator) typingIndicator.hidden = false;

    scrollChatToBottom();

    // Prepare history payload
    const historyPayload = state.chatMessages.slice(-8).map((m) => ({
      role: m.role,
      content: m.content
    }));

    const lessonId = (state.activeView === 'reader' && state.currentLesson?.id) ? state.currentLesson.id : state.chatContextLessonId;
    const endpoint = lessonId ? `/lessons/${lessonId}/chat` : '/lessons/chat';

    const payload = {
      sessionId: state.chatSessionId,
      lessonId: lessonId,
      bookId: state.activeBook?.id || null,
      message: userText,
      history: historyPayload
    };

    try {
      const response = await api(endpoint, {
        method: 'POST',
        body: JSON.stringify(payload)
      });

      if (typingIndicator) typingIndicator.hidden = true;
      state.isChatLoading = false;

      let replyText = response?.reply;
      let sourcesCited = response?.sourcesCited || [];
      let suggestedQuestions = response?.suggestedQuestions || [];

      if (response?.sessionId) {
        state.chatSessionId = response.sessionId;
      }

      if (response?.contextSummary) {
        updateChatContextDisplay(response.contextSummary);
      }

      // Defensive unwrapper if response is stringified or reply contains JSON
      if (typeof replyText === 'string' && (replyText.trim().startsWith('{') && replyText.trim().endsWith('}'))) {
        try {
          const inner = JSON.parse(replyText.trim());
          if (inner.reply) {
            replyText = inner.reply;
            if (inner.sourcesCited && (!sourcesCited || sourcesCited.length === 0)) sourcesCited = inner.sourcesCited;
            if (inner.suggestedQuestions && (!suggestedQuestions || suggestedQuestions.length === 0)) suggestedQuestions = inner.suggestedQuestions;
          }
        } catch {
          // not valid json, keep as is
        }
      }

      if (!replyText && typeof response === 'string') {
        try {
          const parsed = JSON.parse(response);
          replyText = parsed.reply || response;
          if (parsed.sourcesCited) sourcesCited = parsed.sourcesCited;
          if (parsed.suggestedQuestions) suggestedQuestions = parsed.suggestedQuestions;
        } catch {
          replyText = response;
        }
      }

      replyText = replyText || 'تمت مدارسة استفسارك بنجاح.';

      const tutorMsg = {
        role: 'assistant',
        content: replyText,
        sources: sourcesCited
      };

      state.chatMessages.push(tutorMsg);
      appendChatMessageElement(tutorMsg);

      if (suggestedQuestions && suggestedQuestions.length > 0) {
        updateSuggestedPrompts(suggestedQuestions);
      }

      // Refresh sessions dropdown without interrupting the view
      loadChatSessions(state.chatSessionId);

      scrollChatToBottom();
    } catch (err) {
      if (typingIndicator) typingIndicator.hidden = true;
      state.isChatLoading = false;

      const errorMsg = {
        role: 'assistant',
        content: `عذراً، حدث خطأ أثناء معالجة الاستفسار: ${err.message || 'يرجى التأكد من تشغيل الخادم وتوفير مفتاح الذكاء الاصطناعي.'}`
      };
      state.chatMessages.push(errorMsg);
      appendChatMessageElement(errorMsg);
      scrollChatToBottom();
    }
  }

  function appendChatMessageElement(msg) {
    const container = $('#lesson-chat-messages');
    if (!container) return;

    const isUser = msg.role === 'user';
    const item = document.createElement('div');
    item.className = `chat-message-item ${isUser ? 'user-message' : 'tutor-message'}`;

    const formattedContent = isUser ? `<p>${escapeHtml(msg.content)}</p>` : renderChatMarkdown(msg.content);

    const sources = msg.sources || msg.sourcesCited || [];
    let sourcesHtml = '';
    if (!isUser && sources && sources.length > 0) {
      sourcesHtml = `
        <div class="chat-sources-group">
          <span class="chat-sources-label">المراجع:</span>
          ${sources.map((s) => `<span class="chat-source-tag">${escapeHtml(s)}</span>`).join('')}
        </div>
      `;
    }

    item.innerHTML = `
      <div class="message-avatar">${isUser ? '👤' : '🧠'}</div>
      <div class="message-bubble">
        <div class="message-header">
          <strong>${isUser ? 'أنت' : 'المعلم الذكي'}</strong>
          <span>${msg.createdAtUtc ? new Date(msg.createdAtUtc).toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' }) : formatCurrentTime()}</span>
        </div>
        <div class="message-text">
          ${formattedContent}
          ${sourcesHtml}
        </div>
      </div>
    `;

    container.appendChild(item);
  }

  function updateSuggestedPrompts(suggestions) {
    const container = $('#lesson-chat-suggestions');
    if (!container || !suggestions || suggestions.length === 0) return;

    container.innerHTML = suggestions.map((q) => `
      <button type="button" class="chat-suggest-chip" data-question="${escapeHtml(q)}">💡 ${escapeHtml(q)}</button>
    `).join('');

    wireSuggestionChips();
  }

  function scrollChatToBottom() {
    const container = $('#lesson-chat-messages');
    if (container) {
      container.scrollTop = container.scrollHeight;
    }
  }

  function formatCurrentTime() {
    const now = new Date();
    return now.toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
  }

  function renderChatMarkdown(raw) {
    if (!raw) return '';
    let text = escapeHtml(raw);

    // Hadith / Classical quotes « ... »
    text = text.replace(/«([^»]+)»/g, '<span class="diff-highlight">« $1 »</span>');

    // Bold **text**
    text = text.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

    // Inline code `code`
    text = text.replace(/`([^`]+)`/g, '<code class="chat-inline-code">$1</code>');

    // Headings # through ####
    text = text.replace(/^#### (.*$)/gim, '<h6 class="chat-heading chat-h4">$1</h6>');
    text = text.replace(/^### (.*$)/gim, '<h5 class="chat-heading chat-h3">$1</h5>');
    text = text.replace(/^## (.*$)/gim, '<h4 class="chat-heading chat-h2">$1</h4>');
    text = text.replace(/^# (.*$)/gim, '<h4 class="chat-heading chat-h1">$1</h4>');

    // Horizontal rules (--- or ***)
    text = text.replace(/^\s*(?:---|\*\*\*|___)\s*$/gim, '<hr class="chat-divider"/>');

    // Blockquotes > text
    text = text.replace(/^>\s+(.*$)/gim, '<blockquote class="chat-blockquote">$1</blockquote>');

    // Numbered list items: 1. text
    text = text.replace(/^\s*(\d+)\.\s+(.*$)/gim, '<li data-type="ol">$2</li>');

    // Bullet list items: - text or * text
    text = text.replace(/^\s*[-*]\s+(.*$)/gim, '<li data-type="ul">$1</li>');

    // Split paragraphs by double line breaks
    const blocks = text.split(/\n\n+/);
    return blocks.map((block) => {
      const trimmed = block.trim();
      if (!trimmed) return '';
      if (trimmed.startsWith('<h') || trimmed.startsWith('<hr') || trimmed.startsWith('<blockquote')) {
        return trimmed;
      }
      if (trimmed.includes('<li data-type="ol">')) {
        const cleaned = trimmed.replace(/<li data-type="ol">/g, '<li>');
        return `<ol class="chat-ol">${cleaned}</ol>`;
      }
      if (trimmed.includes('<li data-type="ul">') || trimmed.includes('<li>')) {
        const cleaned = trimmed.replace(/<li data-type="ul">/g, '<li>');
        return `<ul class="chat-ul">${cleaned}</ul>`;
      }
      return `<p>${trimmed.replace(/\n/g, '<br/>')}</p>`;
    }).join('');
  }

  // ==========================================================================
  // SCRATCH LEARNING & INTERACTIVE ROADMAP ENGINE (شرح من الصفر)
  // ==========================================================================

  function initScratchLearning() {
    // 1. Restore completed milestones from localStorage
    try {
      const saved = localStorage.getItem('bukhari_scratch_completed');
      if (saved) {
        const parsed = JSON.parse(saved);
        if (Array.isArray(parsed)) {
          scratchState.completedMilestones = new Set(parsed);
        }
      }
    } catch (e) {
      console.warn('Failed to parse saved scratch completions', e);
    }

    // 2. Search & Generate Events
    const topicInput = $('#scratch-topic-input');
    const generateBtn = $('#scratch-generate-btn');

    topicInput?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        const query = topicInput.value.trim();
        if (query) generateScratchRoadmap(query);
      }
    });

    generateBtn?.addEventListener('click', () => {
      const query = topicInput?.value?.trim();
      if (query) generateScratchRoadmap(query);
    });

    // 3. Curated Track Chip Buttons
    $$('.scratch-track-chip').forEach((chip) => {
      chip.addEventListener('click', () => {
        const trackId = chip.dataset.track;
        if (trackId) {
          $$('.scratch-track-chip').forEach(c => c.classList.remove('active'));
          chip.classList.add('active');
          if (topicInput) topicInput.value = '';
          loadScratchTrack(trackId);
        }
      });
    });

    // 4. Milestone Complete Toggle Button
    $('#milestone-mark-complete-btn')?.addEventListener('click', () => {
      if (!scratchState.activeMilestoneId) return;
      const mId = scratchState.activeMilestoneId;
      if (scratchState.completedMilestones.has(mId)) {
        scratchState.completedMilestones.delete(mId);
      } else {
        scratchState.completedMilestones.add(mId);
      }
      saveScratchCompletions();
      updateMilestoneCompleteButtonUI(mId);
      updateScratchTimelineUI();
      updateScratchProgress();
    });

    // 5. Prev / Next Milestone Navigation Buttons
    $('#milestone-prev-btn')?.addEventListener('click', () => navigateMilestone(-1));
    $('#milestone-next-btn')?.addEventListener('click', () => navigateMilestone(1));

    // 6. Export Summary Button
    $('#scratch-export-summary-btn')?.addEventListener('click', () => {
      window.print();
    });

    // 7. Scratch Tutor Assistant Events
    const tutorInput = $('#scratch-tutor-input');
    const tutorBtn = $('#scratch-tutor-ask-btn');

    tutorInput?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        askScratchTutor();
      }
    });

    tutorBtn?.addEventListener('click', () => {
      askScratchTutor();
    });

    // 8. Retry Button
    $('#scratch-retry-btn')?.addEventListener('click', () => {
      if (scratchState.lastQuery) {
        generateScratchRoadmap(scratchState.lastQuery);
      } else {
        loadScratchTrack('mustalah-hadith');
      }
    });
  }

  function saveScratchCompletions() {
    try {
      localStorage.setItem('bukhari_scratch_completed', JSON.stringify(Array.from(scratchState.completedMilestones)));
    } catch (e) {
      console.warn('Failed to save scratch completions', e);
    }
  }

  async function loadScratchTrack(trackId) {
    scratchState.isLoading = true;
    scratchState.lastQuery = trackId;
    showScratchStatus('loading');

    try {
      const roadmap = await api('/scratch/roadmap', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ trackId: trackId, topic: '' })
      });

      renderScratchRoadmap(roadmap);
      showScratchStatus('content');
    } catch (err) {
      console.error('Failed to load scratch track:', err);
      showScratchStatus('error', err.message || 'تعذر تحميل المسار التأسيسي');
    } finally {
      scratchState.isLoading = false;
    }
  }

  async function generateScratchRoadmap(topic) {
    if (!topic || !topic.trim()) return;
    const sanitized = topic.trim();
    scratchState.isLoading = true;
    scratchState.lastQuery = sanitized;

    const spinner = $('#scratch-generate-spinner');
    const btnText = $('#scratch-generate-text');
    if (spinner) spinner.hidden = false;
    if (btnText) btnText.textContent = 'جاري التوليد...';
    showScratchStatus('loading');

    try {
      const roadmap = await api('/scratch/roadmap', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topic: sanitized })
      });

      renderScratchRoadmap(roadmap);
      showScratchStatus('content');
    } catch (err) {
      console.error('Failed to generate scratch roadmap:', err);
      showScratchStatus('error', err.message || 'تعذر توليد خريطة الطريق للموضوع المحدد');
    } finally {
      scratchState.isLoading = false;
      if (spinner) spinner.hidden = true;
      if (btnText) btnText.textContent = 'رسم خريطة الطريق 🗺️';
    }
  }

  function showScratchStatus(status, errorMessage = '') {
    const loadingCard = $('#scratch-status-loading');
    const errorCard = $('#scratch-status-error');
    const contentContainer = $('#scratch-content-container');

    if (loadingCard) loadingCard.hidden = status !== 'loading';
    if (errorCard) {
      errorCard.hidden = status !== 'error';
      if (status === 'error') {
        const errorDetail = $('#scratch-error-detail');
        if (errorDetail) errorDetail.textContent = errorMessage;
      }
    }
    if (contentContainer) contentContainer.hidden = status !== 'content';
  }

  function renderScratchRoadmap(roadmap) {
    if (!roadmap || !roadmap.levels || roadmap.levels.length === 0) {
      showScratchStatus('error', 'البيانات المستلمة لخارطة الطريق غير مكتملة.');
      return;
    }

    scratchState.currentRoadmap = roadmap;

    // 1. Overview Meta Card
    const topicBadge = $('#roadmap-topic-badge');
    const audienceBadge = $('#roadmap-audience-badge');
    const timeBadge = $('#roadmap-time-badge');
    const titleEl = $('#roadmap-title');
    const introEl = $('#roadmap-intro');

    if (topicBadge) topicBadge.textContent = `مسار: ${roadmap.topic || 'تأسيس شامل'}`;
    if (audienceBadge) audienceBadge.textContent = `الفئة: ${roadmap.targetAudience || 'يبدأ من الصفر 🟢'}`;
    if (timeBadge) timeBadge.textContent = `⏱️ ${roadmap.estimatedTotalTimeMinutes || 40} دقيقة تقديرية`;
    if (titleEl) titleEl.textContent = roadmap.title || `خريطة التأسيس في ${roadmap.topic}`;
    if (introEl) introEl.textContent = roadmap.introduction || '';

    // 2. Render Levels Navigation Bar (4 Levels)
    const levelsNav = $('#scratch-levels-nav');
    if (levelsNav) {
      levelsNav.innerHTML = '';
      roadmap.levels.forEach((lvl, idx) => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `scratch-level-tab-btn ${idx === 0 ? 'active' : ''}`;
        btn.dataset.level = String(lvl.levelNumber);
        btn.innerHTML = `
          <span class="level-tab-badge">${lvl.badge || `المستوى ${lvl.levelNumber}`}</span>
          <strong class="level-tab-title">${escapeHtml(lvl.levelName)}</strong>
        `;
        btn.addEventListener('click', () => {
          selectScratchLevel(lvl.levelNumber);
        });
        levelsNav.appendChild(btn);
      });
    }

    // 3. Select First Level
    selectScratchLevel(roadmap.levels[0].levelNumber);
    updateScratchProgress();
  }

  function selectScratchLevel(levelNumber) {
    scratchState.activeLevelNumber = levelNumber;

    // Update level tab buttons UI
    $$('.scratch-level-tab-btn').forEach((btn) => {
      const isMatch = Number(btn.dataset.level) === levelNumber;
      btn.classList.toggle('active', isMatch);
    });

    const level = scratchState.currentRoadmap?.levels?.find(l => l.levelNumber === levelNumber);
    if (!level) return;

    // Update level header & objective
    const lvlBadge = $('#current-level-badge');
    const lvlObj = $('#current-level-objective');
    if (lvlBadge) lvlBadge.textContent = level.badge || `المستوى ${level.levelNumber}`;
    if (lvlObj) lvlObj.textContent = level.objective || '';

    // Render Milestones in Sidebar
    renderScratchMilestonesTimeline(level);

    // Select first milestone of this level
    if (level.milestones && level.milestones.length > 0) {
      selectScratchMilestone(level.milestones[0].id);
    }
  }

  function renderScratchMilestonesTimeline(level) {
    const listEl = $('#milestones-timeline-list');
    if (!listEl) return;

    listEl.innerHTML = '';
    (level.milestones || []).forEach((m) => {
      const isCompleted = scratchState.completedMilestones.has(m.id);
      const item = document.createElement('div');
      item.className = `milestone-nav-item ${scratchState.activeMilestoneId === m.id ? 'active' : ''}`;
      item.dataset.milestoneId = m.id;
      item.innerHTML = `
        <span class="milestone-nav-icon">${m.icon || '📌'}</span>
        <div class="milestone-nav-info">
          <div class="milestone-nav-title">${escapeHtml(m.title)}</div>
          <div class="milestone-nav-term">${escapeHtml(m.keyTerm || m.shortSummary || '')}</div>
        </div>
        <span class="milestone-nav-status" id="milestone-status-${m.id}">${isCompleted ? '✓' : '○'}</span>
      `;
      item.addEventListener('click', () => {
        selectScratchMilestone(m.id);
      });
      listEl.appendChild(item);
    });
  }

  function updateScratchTimelineUI() {
    (scratchState.currentRoadmap?.levels || []).forEach((lvl) => {
      (lvl.milestones || []).forEach((m) => {
        const isCompleted = scratchState.completedMilestones.has(m.id);
        const statusEl = $(`#milestone-status-${m.id}`);
        if (statusEl) {
          statusEl.textContent = isCompleted ? '✓' : '○';
        }
      });
    });
  }

  function selectScratchMilestone(milestoneId) {
    scratchState.activeMilestoneId = milestoneId;

    // Highlight active nav item
    $$('.milestone-nav-item').forEach((item) => {
      const isMatch = item.dataset.milestoneId === milestoneId;
      item.classList.toggle('active', isMatch);
    });

    // Find milestone across levels
    let foundMilestone = null;
    let foundLevel = null;

    for (const lvl of scratchState.currentRoadmap?.levels || []) {
      const match = lvl.milestones.find(m => m.id === milestoneId);
      if (match) {
        foundMilestone = match;
        foundLevel = lvl;
        break;
      }
    }

    if (foundMilestone && foundLevel) {
      renderActiveMilestone(foundMilestone, foundLevel);
    }
  }

  function renderActiveMilestone(milestone, level) {
    const exp = milestone.explanation || {};

    // 1. Header Info
    const iconEl = $('#active-milestone-icon');
    const levelLabel = $('#active-milestone-level');
    const titleEl = $('#active-milestone-title');

    if (iconEl) iconEl.textContent = milestone.icon || '📌';
    if (levelLabel) levelLabel.textContent = `${level.levelName} (${level.badge || ''})`;
    if (titleEl) {
      titleEl.textContent = milestone.title;
    }

    // 2. Complete Button State
    updateMilestoneCompleteButtonUI(milestone.id);

    // 3. Simple Concept (ELI5)
    const simpleConceptEl = $('#active-simple-concept');
    if (simpleConceptEl) {
      simpleConceptEl.textContent = exp.simpleConcept || milestone.shortSummary || 'الفكرة الأساسية تهدف لبناء المفهوم بيسر وسهولة.';
    }

    // 4. Real-World Analogy
    const analogyEl = $('#active-analogy');
    if (analogyEl) {
      analogyEl.textContent = exp.realWorldAnalogy || 'تشبيه واقعي يقرب المعنى المجرد إلى شيء نلمسه في حياتنا اليومية.';
    }

    // 5. Steps Breakdown
    const stepsListEl = $('#active-steps-list');
    if (stepsListEl) {
      stepsListEl.innerHTML = '';
      const steps = exp.steps || [];
      if (steps.length === 0) {
        stepsListEl.innerHTML = '<div class="step-item-card"><div class="step-card-header"><span class="step-number-badge">1</span><span>الاستيعاب</span></div><p class="step-card-desc">قراءة المفهوم وتأمله بهدوء.</p></div>';
      } else {
        steps.forEach((st) => {
          const card = document.createElement('div');
          card.className = 'step-item-card';
          card.innerHTML = `
            <div class="step-card-header">
              <span class="step-number-badge">${st.stepNumber || 1}</span>
              <span>${escapeHtml(st.title || 'خطوة')}</span>
            </div>
            <p class="step-card-desc">${escapeHtml(st.explanation || '')}</p>
          `;
          stepsListEl.appendChild(card);
        });
      }
    }

    // 6. Common Pitfalls
    const pitfallsSection = $('#active-pitfalls-section');
    const pitfallsListEl = $('#active-pitfalls-list');
    if (pitfallsListEl) {
      pitfallsListEl.innerHTML = '';
      const pitfalls = exp.commonPitfalls || [];
      if (pitfalls.length > 0) {
        if (pitfallsSection) pitfallsSection.hidden = false;
        pitfalls.forEach((pitfall) => {
          const li = document.createElement('li');
          li.className = 'pitfall-item';
          li.innerHTML = `<span>⚠️</span><span>${escapeHtml(pitfall)}</span>`;
          pitfallsListEl.appendChild(li);
        });
      } else {
        if (pitfallsSection) pitfallsSection.hidden = true;
      }
    }

    // 7. Heritage Practical Example
    const heritageSection = $('#active-heritage-section');
    const practicalExampleEl = $('#active-practical-example');
    if (practicalExampleEl) {
      if (exp.practicalExample) {
        if (heritageSection) heritageSection.hidden = false;
        practicalExampleEl.textContent = exp.practicalExample;
      } else {
        if (heritageSection) heritageSection.hidden = true;
      }
    }

    // 8. Checkpoint Verification Quiz
    renderMilestoneQuiz(exp.quiz, milestone.id);

    // 9. Reset Tutor Assistant Box
    const tutorResponseBox = $('#scratch-tutor-response-box');
    const tutorInput = $('#scratch-tutor-input');
    if (tutorResponseBox) tutorResponseBox.hidden = true;
    if (tutorInput) tutorInput.value = '';
  }

  function updateMilestoneCompleteButtonUI(milestoneId) {
    const btn = $('#milestone-mark-complete-btn');
    const textEl = $('#milestone-complete-text');
    if (!btn || !textEl) return;

    const isCompleted = scratchState.completedMilestones.has(milestoneId);
    btn.classList.toggle('completed', isCompleted);
    textEl.textContent = isCompleted ? 'مكتملة ومتقنة ✓' : 'تحديد كمكتمل';
  }

  function renderMilestoneQuiz(quiz, milestoneId) {
    const questionEl = $('#active-quiz-question');
    const optionsEl = $('#active-quiz-options');
    const feedbackBox = $('#active-quiz-feedback');

    if (feedbackBox) feedbackBox.hidden = true;
    if (!optionsEl) return;
    optionsEl.innerHTML = '';

    if (!quiz || !quiz.question || !quiz.options || quiz.options.length === 0) {
      if (questionEl) questionEl.textContent = 'اختبار الفهم السريع: هل استوعبت الفكرة التأسيسية لهذه المحطة؟';
      const yesBtn = document.createElement('button');
      yesBtn.type = 'button';
      yesBtn.className = 'quiz-option-btn';
      yesBtn.innerHTML = '<span>✅</span><span>نعم استوعبتها وجاهز للمحطة التالية</span>';
      yesBtn.addEventListener('click', () => {
        scratchState.completedMilestones.add(milestoneId);
        saveScratchCompletions();
        updateMilestoneCompleteButtonUI(milestoneId);
        updateScratchTimelineUI();
        updateScratchProgress();
        yesBtn.classList.add('correct');
      });
      optionsEl.appendChild(yesBtn);
      return;
    }

    if (questionEl) questionEl.textContent = quiz.question;

    quiz.options.forEach((optText, optIdx) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'quiz-option-btn';
      btn.innerHTML = `<span>${['أ', 'ب', 'ج', 'د'][optIdx] || (optIdx + 1)}.</span><span>${escapeHtml(optText)}</span>`;

      btn.addEventListener('click', () => {
        // Disable all options
        $$('.quiz-option-btn', optionsEl).forEach(b => b.disabled = true);

        const isCorrect = optIdx === quiz.correctIndex;
        btn.classList.add(isCorrect ? 'correct' : 'incorrect');

        if (!isCorrect && quiz.correctIndex >= 0 && quiz.correctIndex < quiz.options.length) {
          const correctBtn = optionsEl.children[quiz.correctIndex];
          if (correctBtn) correctBtn.classList.add('correct');
        }

        // Show Feedback
        if (feedbackBox) {
          feedbackBox.hidden = false;
          feedbackBox.className = `quiz-feedback-box ${isCorrect ? 'correct' : 'incorrect'}`;

          const headerEl = $('#quiz-feedback-header');
          const explanationEl = $('#active-quiz-explanation');
          const tipEl = $('#active-quiz-tip');

          if (headerEl) headerEl.textContent = isCorrect ? '🎉 إجابة صحيحة وممتازة!' : '💡 محاولة طيبة، إليك التوضيح:';
          if (explanationEl) explanationEl.textContent = quiz.explanation || '';
          if (tipEl) tipEl.textContent = quiz.reinforcementTip ? `⭐ فائدة ذهبية: ${quiz.reinforcementTip}` : '';
        }

        // Auto mark complete on correct answer
        if (isCorrect) {
          scratchState.completedMilestones.add(milestoneId);
          saveScratchCompletions();
          updateMilestoneCompleteButtonUI(milestoneId);
          updateScratchTimelineUI();
          updateScratchProgress();
        }
      });

      optionsEl.appendChild(btn);
    });
  }

  async function askScratchTutor() {
    const input = $('#scratch-tutor-input');
    const question = input?.value?.trim();
    if (!question) return;

    if (scratchState.isTutorLoading) return;
    scratchState.isTutorLoading = true;

    const spinner = $('#scratch-tutor-spinner');
    if (spinner) spinner.hidden = false;

    const responseBox = $('#scratch-tutor-response-box');
    const answerEl = $('#scratch-tutor-answer');
    const analogyEl = $('#scratch-tutor-analogy');
    const analogyBox = $('#scratch-tutor-analogy-box');
    const takeawayEl = $('#scratch-tutor-takeaway');

    try {
      const activeMilestone = findActiveMilestone();
      const payload = {
        topic: scratchState.currentRoadmap?.topic || 'العلوم التراثية',
        currentMilestone: activeMilestone?.title || 'المحطة التأسيسية',
        userQuestion: question,
        simplicityMode: 'أبسط ما يمكن وبدون تعقيد'
      };

      const result = await api('/scratch/ask-tutor', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });

      if (responseBox) responseBox.hidden = false;
      if (answerEl) answerEl.textContent = result.answer || '';

      if (analogyEl && analogyBox) {
        if (result.simplifiedAnalogy) {
          analogyBox.hidden = false;
          analogyEl.textContent = result.simplifiedAnalogy;
        } else {
          analogyBox.hidden = true;
        }
      }

      if (takeawayEl) takeawayEl.textContent = result.keyTakeaway || '';
    } catch (err) {
      console.error('Failed to ask scratch tutor:', err);
      if (responseBox) responseBox.hidden = false;
      if (answerEl) answerEl.textContent = 'المعنى باختصار: هذا المفهوم في أصله قاعدة بديهية، نفهمها خطوة بخطوة ونطبق عليها بأمثلة ميسرة.';
    } finally {
      scratchState.isTutorLoading = false;
      if (spinner) spinner.hidden = true;
    }
  }

  function findActiveMilestone() {
    if (!scratchState.activeMilestoneId) return null;
    for (const lvl of scratchState.currentRoadmap?.levels || []) {
      const match = lvl.milestones.find(m => m.id === scratchState.activeMilestoneId);
      if (match) return match;
    }
    return null;
  }

  function navigateMilestone(direction) {
    const allMilestones = [];
    (scratchState.currentRoadmap?.levels || []).forEach((lvl) => {
      (lvl.milestones || []).forEach((m) => {
        allMilestones.push({ milestone: m, level: lvl });
      });
    });

    if (allMilestones.length === 0) return;

    const currentIndex = allMilestones.findIndex(item => item.milestone.id === scratchState.activeMilestoneId);
    if (currentIndex === -1) return;

    const nextIndex = currentIndex + direction;
    if (nextIndex >= 0 && nextIndex < allMilestones.length) {
      const nextItem = allMilestones[nextIndex];
      // Switch level if necessary
      if (nextItem.level.levelNumber !== scratchState.activeLevelNumber) {
        selectScratchLevel(nextItem.level.levelNumber);
      }
      selectScratchMilestone(nextItem.milestone.id);

      // Scroll smoothly to detail panel
      $('#milestone-detail-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }

  function updateScratchProgress() {
    let total = 0;
    (scratchState.currentRoadmap?.levels || []).forEach((lvl) => {
      total += (lvl.milestones || []).length;
    });

    if (total === 0) total = 8;

    let completed = 0;
    (scratchState.currentRoadmap?.levels || []).forEach((lvl) => {
      (lvl.milestones || []).forEach((m) => {
        if (scratchState.completedMilestones.has(m.id)) {
          completed++;
        }
      });
    });

    const percent = Math.min(100, Math.round((completed / total) * 100));

    const percentEl = $('#roadmap-progress-percent');
    const fillEl = $('#roadmap-progress-fill');
    const completedCountEl = $('#roadmap-completed-count');
    const totalCountEl = $('#roadmap-total-count');

    if (percentEl) percentEl.textContent = `${percent}%`;
    if (fillEl) fillEl.style.width = `${percent}%`;
    if (completedCountEl) completedCountEl.textContent = String(completed);
    if (totalCountEl) totalCountEl.textContent = String(total);
  }

  // Run on DOM Ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
