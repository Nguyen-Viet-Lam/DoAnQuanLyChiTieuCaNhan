(function () {
  const desktopSidebarKey = "smartspend.sidebar.desktopCollapsed";
  const toastDuration = 4200;
  const state = {
    currentUser: null,
    alerts: [],
    alertPollingId: null,
    hubConnection: null,
  };

  function normalizeRole(role) {
    const value = String(role || "").toLowerCase();
    if (value === "systemadmin" || value === "admin") {
      return "Quản trị viên";
    }

    if (value === "standarduser" || value === "user") {
      return "Người dùng";
    }

    return "Khách";
  }

  function isAdminRole(role) {
    return normalizeRole(role) === "Quản trị viên";
  }

  function setTheme(theme) {
    const nextTheme = theme === "dark" ? "light" : "light";
    document.body.dataset.theme = nextTheme;
    document.documentElement.dataset.theme = nextTheme;
  }

  function initTheme() {
    setTheme("light");
  }

  function getInitials(user) {
    const source = String(user?.fullName || user?.username || user?.email || "SS").trim();
    if (!source) {
      return "SS";
    }

    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    }

    return source.slice(0, 2).toUpperCase();
  }

  function getAvatarMarkup(user) {
    const avatarUrl = String(user?.avatarUrl || "").trim();
    if (avatarUrl) {
      return `<img class="topbar-avatar-image" src="${escapeHtml(avatarUrl)}" alt="Avatar" loading="lazy">`;
    }

    return `<span class="avatar-dot topbar-avatar-dot">${escapeHtml(getInitials(user))}</span>`;
  }

  function formatCurrency(value) {
    const number = Number(value || 0);
    return new Intl.NumberFormat("vi-VN", {
      style: "currency",
      currency: "VND",
      maximumFractionDigits: 0,
    }).format(number);
  }

  function formatDate(value) {
    if (!value) {
      return "--";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "--";
    }

    return new Intl.DateTimeFormat("vi-VN", {
      dateStyle: "medium",
    }).format(date);
  }

  function formatDateTime(value) {
    if (!value) {
      return "--";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "--";
    }

    return new Intl.DateTimeFormat("vi-VN", {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(date);
  }

  function formatDateInput(value) {
    if (!value) {
      return "";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "";
    }

    return date.toISOString().slice(0, 10);
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/\"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function extractErrorMessage(data, fallback) {
    if (!data) {
      return fallback || "Yêu cầu thất bại.";
    }

    if (typeof data === "string") {
      return data;
    }

    if (data.message) {
      return data.message;
    }

    if (data.title) {
      return data.title;
    }

    if (data.errors && typeof data.errors === "object") {
      const messages = Object.values(data.errors)
        .flat()
        .filter(Boolean);
      if (messages.length > 0) {
        return messages.join(" ");
      }
    }

    return fallback || "Yêu cầu thất bại.";
  }

  async function api(path, options) {
    const settings = options || {};
    const headers = new Headers(settings.headers || {});
    const token = window.AuthClient?.getAccessToken?.();

    if (!headers.has("Accept")) {
      headers.set("Accept", "application/json");
    }

    if (token && !headers.has("Authorization")) {
      headers.set("Authorization", `Bearer ${token}`);
    }

    let body = settings.body;
    if (body && !(body instanceof FormData) && typeof body === "object") {
      headers.set("Content-Type", "application/json");
      body = JSON.stringify(body);
    }

    const response = await fetch(path, {
      method: settings.method || "GET",
      headers,
      body,
      credentials: "same-origin",
    });

    const contentType = response.headers.get("content-type") || "";
    const isJson = contentType.toLowerCase().includes("application/json");
    const data = isJson
      ? await response.json().catch(() => null)
      : await response.text().catch(() => null);

    if (!response.ok) {
      if (response.status === 401 && window.AuthClient) {
        window.AuthClient.clearSession();
        window.location.href = "/home/login.html?message=Phiên đăng nhập đã hết hạn.";
      }

      const error = new Error(extractErrorMessage(data, `Yêu cầu thất bại (${response.status}).`));
      error.status = response.status;
      error.data = data;
      throw error;
    }

    return data;
  }

  function showToast(message, tone) {
    const stack = document.getElementById("toastStack") || createToastStack();
    const item = document.createElement("div");
    item.className = `toast ${tone || "info"}`;
    item.innerHTML = `<strong>${escapeHtml(message)}</strong>`;
    stack.appendChild(item);

    window.setTimeout(() => {
      item.remove();
    }, toastDuration);
  }

  function createToastStack() {
    const stack = document.createElement("div");
    stack.id = "toastStack";
    stack.className = "toast-stack";
    document.body.appendChild(stack);
    return stack;
  }

  function setCurrentYear() {
    document.querySelectorAll("[data-current-year]").forEach((element) => {
      element.textContent = String(new Date().getFullYear());
    });
  }

  function isMobileSidebarLayout() {
    return window.matchMedia("(max-width: 940px)").matches;
  }

  function getDesktopSidebarCollapsed() {
    return window.localStorage.getItem(desktopSidebarKey) === "1";
  }

  function setDesktopSidebarCollapsed(collapsed) {
    if (collapsed) {
      window.localStorage.setItem(desktopSidebarKey, "1");
    } else {
      window.localStorage.removeItem(desktopSidebarKey);
    }
  }

  function getPageTitle() {
    return document.body.dataset.pageTitle || "SmartSpend AI";
  }

  function isAdminPage() {
    return String(document.body.dataset.requiredRole || "").toLowerCase() === "systemadmin";
  }

  async function fetchProfile() {
    try {
      return await api("/api/profile");
    } catch {
      return null;
    }
  }

  function getNavSections(role) {
    const userItems = [
      { href: "/home/dashboard.html", label: "Tổng quan", key: "dashboard" },
      { href: "/home/wallets.html", label: "Ví", key: "wallets" },
      { href: "/home/transactions.html", label: "Giao dịch", key: "transactions" },
      { href: "/home/budgets.html", label: "Ngân sách", key: "budgets" },
      { href: "/home/reports.html", label: "Báo cáo", key: "reports" },
      { href: "/home/profile.html", label: "Hồ sơ", key: "profile" },
    ];

    const adminItems = [
      { href: "/home/admin-dashboard.html", label: "Tổng quan hệ thống", key: "admin-dashboard" },
      { href: "/home/admin-users.html", label: "Người dùng", key: "admin-users" },
      { href: "/home/admin-categories.html", label: "Danh mục", key: "admin-categories" },
      { href: "/home/admin-logs.html", label: "Nhật ký hệ thống", key: "admin-logs" },
    ];

    if (isAdminRole(role)) {
      return [{ title: "Quản trị", items: adminItems }];
    }

    return [{ title: "Người dùng", items: userItems }];
  }

  function getSidebarIcon(key) {
    const icons = {
      dashboard:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="8" height="8" rx="2"></rect><rect x="13" y="3" width="8" height="5" rx="2"></rect><rect x="13" y="10" width="8" height="11" rx="2"></rect><rect x="3" y="13" width="8" height="8" rx="2"></rect></svg>',
      wallets:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 8a3 3 0 0 1 3-3h11a2 2 0 0 1 0 4H6a3 3 0 0 0-3 3z"></path><path d="M3 8v8a3 3 0 0 0 3 3h13a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2H6"></path><circle cx="17" cy="14" r="1"></circle></svg>',
      transactions:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M7 3h8l4 4v14H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"></path><path d="M15 3v5h5"></path><path d="M9 12h6"></path><path d="M9 16h6"></path></svg>',
      budgets:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 14h4v6H4z"></path><path d="M10 10h4v10h-4z"></path><path d="M16 5h4v15h-4z"></path></svg>',
      profile:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="8" r="4"></circle><path d="M4 20a8 8 0 0 1 16 0"></path></svg>',
      reports:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 19h16"></path><path d="M7 16V9"></path><path d="M12 16V5"></path><path d="M17 16v-3"></path></svg>',
      "admin-dashboard":
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l8 4v6c0 4.6-3.2 7.8-8 8-4.8-.2-8-3.4-8-8V7l8-4z"></path><path d="M9 12l2 2 4-4"></path></svg>',
      "admin-users":
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2"></path><circle cx="9.5" cy="7" r="3.5"></circle><path d="M20 21v-2a4 4 0 0 0-3-3.87"></path><path d="M15 4.13a3.5 3.5 0 0 1 0 5.74"></path></svg>',
      "admin-categories":
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6h7"></path><path d="M4 12h16"></path><path d="M4 18h10"></path><circle cx="15" cy="6" r="2"></circle><circle cx="18" cy="18" r="2"></circle></svg>',
      "admin-logs":
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M5 4h11l3 3v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"></path><path d="M14 4v4h4"></path><path d="M8 12h8"></path><path d="M8 16h8"></path></svg>',
      guide:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15.5A2.5 2.5 0 0 0 17.5 16H4z"></path><path d="M6.5 3A2.5 2.5 0 0 0 4 5.5V21l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V16"></path></svg>',
      home:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 10.5 12 3l9 7.5"></path><path d="M5 9.5V21h14V9.5"></path></svg>',
      logout:
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path><path d="M16 17l5-5-5-5"></path><path d="M21 12H9"></path></svg>',
    };

    return icons[key] || icons.dashboard;
  }

  function renderSidebar(user) {
    const sidebar = document.getElementById("appSidebar");
    if (!sidebar) {
      return;
    }

    const activeKey = document.body.dataset.page || "dashboard";
    const sections = getNavSections(user.role);

    const adminContext = isAdminRole(user.role) && isAdminPage();
    const sidebarHeader = adminContext
      ? `
        <div class="sidebar-brand-card">
          <span class="brand-mark">SA</span>
          <div class="sidebar-brand-copy">
            <strong>SmartSpend Admin</strong>
            <small>Trung tâm quản trị</small>
          </div>
        </div>`
      : "";

    sidebar.innerHTML = `
      ${sidebarHeader}
      ${sections
        .map(
          (section) => `
            <div class="sidebar-title">${escapeHtml(section.title)}</div>
            <nav class="sidebar-nav">
              ${section.items
                .map(
                  (item) => `
                    <a class="sidebar-link ${item.key === activeKey ? "is-active" : ""}" href="${item.href}">
                      <span class="sidebar-link-icon" aria-hidden="true">${getSidebarIcon(item.key)}</span>
                      <span class="sidebar-link-label">${escapeHtml(item.label)}</span>
                    </a>`
                )
                .join("")}
            </nav>`
        )
        .join("")}
      <div class="sidebar-footer">
        <a class="sidebar-link logout-link" href="#" data-auth-logout>
          <span class="sidebar-link-icon" aria-hidden="true">${getSidebarIcon("logout")}</span>
          <span class="sidebar-link-label">Đăng xuất</span>
        </a>
      </div>`;

    sidebar.querySelectorAll("[data-auth-logout]").forEach((element) => {
      element.addEventListener("click", async (event) => {
        event.preventDefault();
        await window.AuthClient?.logout?.();
        window.location.href = "/home/login.html";
      });
    });
  }

  function renderTopbar(user) {
    const topbar = document.getElementById("appTopbar");
    if (!topbar) {
      return;
    }

    const roleLabel = user.roleDisplay || normalizeRole(user.role);
    const adminContext = isAdminRole(user.role) && isAdminPage();
    const brandTitle = adminContext ? "Trung tâm quản trị" : "SmartSpend AI";
    const brandSubtitle = adminContext ? "Khu vực điều hành hệ thống" : "Không gian tài chính";
    const brandHref = adminContext ? "/home/admin-dashboard.html" : "/home/dashboard.html";
    const adminRibbon = adminContext
      ? ""
      : `
            <a class="topbar-pill topbar-brand-pill" href="${brandHref}">
              <span class="brand-mark">S</span>
              <span class="topbar-pill-copy">
                <strong>${escapeHtml(brandTitle)}</strong>
                <small>${escapeHtml(brandSubtitle)}</small>
              </span>
            </a>`;

    topbar.innerHTML = `
      <div class="topbar-meta">
        <button class="icon-button sidebar-toggle" id="sidebarToggle" type="button" aria-label="Mở menu" aria-controls="appSidebar" aria-expanded="true">☰</button>
        <div class="topbar-overview">
          <div class="topbar-ribbon">
            ${adminRibbon}
            <div class="topbar-pill topbar-user-pill">
              ${getAvatarMarkup(user)}
              <span class="topbar-pill-copy">
                <strong>${escapeHtml(user.fullName || user.username || "Người dùng")}</strong>
                <small>${escapeHtml(roleLabel)}</small>
              </span>
            </div>
          </div>
          <div class="topbar-copy">
            <h1>${escapeHtml(getPageTitle())}</h1>
            <p>${escapeHtml(user.email || "Theo dõi người dùng, danh mục và nhật ký hệ thống")}</p>
          </div>
        </div>
      </div>
      <div class="inline-actions">
        <div style="position: relative; ${adminContext ? "display:none;" : ""}">
          <button class="icon-button alert-button" id="alertToggle" type="button">
            Cảnh báo <span class="alert-count" id="alertCount">0</span>
          </button>
          <div class="alert-dropdown" id="alertDropdown">
            <div class="card-header">
              <strong>Thông báo ngân sách</strong>
              <button class="btn btn-ghost" type="button" id="refreshAlertsButton">Làm mới</button>
            </div>
            <div id="alertList" class="alert-list"></div>
          </div>
        </div>
      </div>`;

    initTheme();
    bindShellControls();
  }

  function bindShellControls() {
    const sidebarToggle = document.getElementById("sidebarToggle");
    const sidebar = document.getElementById("appSidebar");
    const backdrop = document.getElementById("appBackdrop");
    const alertToggle = document.getElementById("alertToggle");
    const alertDropdown = document.getElementById("alertDropdown");
    const refreshAlertsButton = document.getElementById("refreshAlertsButton");
    const edgeToggle = ensureDesktopSidebarToggle();

    const syncSidebarControls = () => {
      const isMobile = isMobileSidebarLayout();
      const isExpanded = isMobile
        ? Boolean(sidebar?.classList.contains("is-open"))
        : !document.body.classList.contains("app-sidebar-collapsed");

      if (sidebarToggle) {
        sidebarToggle.textContent = isMobile && isExpanded ? "✕" : "☰";
        sidebarToggle.setAttribute("aria-expanded", String(isExpanded));
      }

      if (edgeToggle) {
        edgeToggle.setAttribute("aria-expanded", String(isExpanded));
        edgeToggle.classList.toggle("is-active", isExpanded);

        const label = edgeToggle.querySelector("[data-edge-label]");
        const icon = edgeToggle.querySelector("[data-edge-icon]");
        if (label) {
          label.textContent = isExpanded ? "Đóng" : "Menu";
        }
        if (icon) {
          icon.textContent = isExpanded ? "✕" : "☰";
        }
      }
    };

    const closeMobileSidebar = () => {
      if (!sidebar || !backdrop) {
        return;
      }

      sidebar.classList.remove("is-open");
      backdrop.classList.remove("is-visible");
      syncSidebarControls();
    };

    const toggleSidebar = () => {
      if (!sidebar) {
        return;
      }

      if (isMobileSidebarLayout()) {
        const shouldOpen = !sidebar.classList.contains("is-open");
        sidebar.classList.toggle("is-open", shouldOpen);
        backdrop?.classList.toggle("is-visible", shouldOpen);
      } else {
        const nextCollapsed = !document.body.classList.contains("app-sidebar-collapsed");
        document.body.classList.toggle("app-sidebar-collapsed", nextCollapsed);
        setDesktopSidebarCollapsed(nextCollapsed);
      }

      syncSidebarControls();
    };

    if (!isMobileSidebarLayout()) {
      document.body.classList.toggle("app-sidebar-collapsed", getDesktopSidebarCollapsed());
    } else {
      closeMobileSidebar();
    }

    syncSidebarControls();

    if (sidebarToggle && sidebar && backdrop) {
      sidebarToggle.addEventListener("click", toggleSidebar);
      backdrop.addEventListener("click", closeMobileSidebar);
    }

    if (edgeToggle) {
      edgeToggle.addEventListener("click", toggleSidebar);
    }

    if (alertToggle && alertDropdown) {
      alertToggle.addEventListener("click", () => {
        alertDropdown.classList.toggle("is-open");
      });

      document.addEventListener("click", (event) => {
        if (!alertDropdown.contains(event.target) && !alertToggle.contains(event.target)) {
          alertDropdown.classList.remove("is-open");
        }
      });
    }

    if (refreshAlertsButton) {
      refreshAlertsButton.addEventListener("click", () => {
        refreshAlerts().catch((error) => showToast(error.message, "error"));
      });
    }

    window.addEventListener("resize", () => {
      if (isMobileSidebarLayout()) {
        closeMobileSidebar();
      } else {
        backdrop?.classList.remove("is-visible");
        sidebar?.classList.remove("is-open");
        document.body.classList.toggle("app-sidebar-collapsed", getDesktopSidebarCollapsed());
        syncSidebarControls();
      }
    });
  }

  function ensureDesktopSidebarToggle() {
    if ((document.body.dataset.layout || "public") !== "app") {
      return null;
    }

    let button = document.getElementById("sidebarEdgeToggle");
    if (button) {
      return button;
    }

    button = document.createElement("button");
    button.type = "button";
    button.id = "sidebarEdgeToggle";
    button.className = "sidebar-edge-toggle";
    button.setAttribute("aria-label", "Đóng mở thanh điều hướng");
    button.setAttribute("aria-controls", "appSidebar");
    button.innerHTML = `
      <span class="sidebar-edge-toggle-icon" data-edge-icon>☰</span>
      <span class="sidebar-edge-toggle-label" data-edge-label>Menu</span>`;
    document.body.appendChild(button);
    return button;
  }

  function renderAlerts() {
    const alertCount = document.getElementById("alertCount");
    const alertList = document.getElementById("alertList");
    if (!alertCount || !alertList) {
      return;
    }

    const unread = state.alerts.filter((item) => !item.isRead).length;
    alertCount.textContent = String(unread);

    if (state.alerts.length === 0) {
      alertList.innerHTML = '<div class="empty-state">Chưa có cảnh báo nào. Khi vượt 80% hoặc 100% ngân sách, thông báo sẽ hiện ở đây.</div>';
      return;
    }

    alertList.innerHTML = state.alerts
      .map((item) => {
        const kind = String(item.kind || item.source || "").toLowerCase();
        const isAiReminder = kind === "aireminder" || kind === "ai";
        const levelClass = item.level === "Danger" ? "status-danger" : item.level === "Warning" ? "status-warning" : "status-safe";
        const label = isAiReminder
          ? "AI"
          : item.level === "Danger"
            ? "Vượt mức"
            : item.level === "Warning"
              ? "Cảnh báo"
              : "Thông tin";
        const sourceLabel = isAiReminder ? '<span class="status-pill status-safe">AI</span>' : "";
        const readButton = !isAiReminder && !item.isRead ? `<button class="btn btn-ghost" data-alert-read="${item.alertId}">Đã đọc</button>` : "";

        return `
          <div class="alert-item">
            <div class="inline-actions" style="justify-content: space-between; align-items: flex-start;">
              <div>
                <div class="inline-actions" style="gap: 8px;">
                  <div class="status-pill ${levelClass}">
                    ${escapeHtml(label)}
                  </div>
                  ${sourceLabel}
                </div>
                <p style="margin: 10px 0 4px;">${escapeHtml(item.message)}</p>
                <div class="muted">${escapeHtml(formatDateTime(item.createdAt))}</div>
              </div>
              ${readButton}
            </div>
          </div>`;
      })
      .join("");

    alertList.querySelectorAll("[data-alert-read]").forEach((button) => {
      button.addEventListener("click", async () => {
        try {
          await api(`/api/alerts/${button.dataset.alertRead}/read`, { method: "POST" });
          await refreshAlerts();
        } catch (error) {
          showToast(error.message, "error");
        }
      });
    });
  }

  async function refreshAlerts() {
    if (!window.AuthClient?.isAuthenticated?.()) {
      return [];
    }

    state.alerts = await api("/api/alerts");
    renderAlerts();
    return state.alerts;
  }

  async function initRealtimeAlerts() {
    if (!window.AuthClient?.isAuthenticated?.()) {
      return;
    }

    await refreshAlerts().catch(() => null);

    if (window.signalR && !state.hubConnection) {
      state.hubConnection = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/budget-alerts", {
          accessTokenFactory: () => window.AuthClient.getAccessToken(),
        })
        .withAutomaticReconnect()
        .build();

      state.hubConnection.on("budgetAlert", (payload) => {
        state.alerts = [
          {
            alertId: payload.alertId,
            message: payload.message,
            level: payload.level,
            createdAt: payload.createdAt,
            isRead: false,
          },
          ...state.alerts,
        ].slice(0, 20);
        renderAlerts();
        showToast(payload.message, payload.level === "Danger" ? "error" : "warning");
      });

      state.hubConnection.on("aiReminder", (payload) => {
        state.alerts = [
          {
            alertId: payload.reminderId,
            message: payload.message,
            level: payload.level || "Info",
            createdAt: payload.createdAt,
            isRead: true,
            kind: payload.kind || "AiReminder",
            source: payload.source || "AI",
          },
          ...state.alerts,
        ].slice(0, 20);
        renderAlerts();
        showToast(payload.message, payload.level === "Serious" ? "warning" : "info");
      });

      try {
        await state.hubConnection.start();
      } catch {
        state.hubConnection = null;
      }
    }

    if (!state.hubConnection && !state.alertPollingId) {
      state.alertPollingId = window.setInterval(() => {
        refreshAlerts().catch(() => null);
      }, 20000);
    }
  }

  async function ensureProtectedLayout() {
    const requiredRole = document.body.dataset.requiredRole || "";
    const me = await window.AuthClient?.requireAuth?.({
      roles: requiredRole ? [requiredRole] : [],
      onForbidden: () => {
        window.location.href = "/home/dashboard.html";
      },
    });

    if (!me) {
      return;
    }

    const profile = await fetchProfile();
    state.currentUser = {
      userId: Number(me.userId || profile?.userId || 0),
      username: me.username || profile?.username || "user",
      fullName: profile?.fullName || window.AuthClient.getCurrentUser()?.fullName || me.username,
      email: me.email || profile?.email || "",
      avatarUrl: profile?.avatarUrl || "",
      role: me.role || window.AuthClient.getCurrentUser()?.role || "StandardUser",
      roleDisplay: me.roleDisplay || window.AuthClient.getCurrentUser()?.roleDisplay || normalizeRole(me.role),
      isAdmin: Boolean(me.isAdmin),
    };

    if (state.currentUser.isAdmin && !isAdminPage()) {
      window.location.href = "/home/admin-dashboard.html";
      return;
    }

    renderSidebar(state.currentUser);
    renderTopbar(state.currentUser);
    await initRealtimeAlerts();

    document.dispatchEvent(
      new CustomEvent("smartspend:ready", {
        detail: {
          currentUser: state.currentUser,
        },
      })
    );
  }

  function initPublicPage() {
    initPublicDrawer();
    document.dispatchEvent(new CustomEvent("smartspend:ready", { detail: { currentUser: null } }));
  }

  function getPublicDrawerLinks() {
    const isAuthed = window.AuthClient?.isAuthenticated?.();
    const baseLinks = [
      { href: "/home/index.html", label: "Trang chủ" },
      { href: "/home/about.html", label: "Giới thiệu" },
      { href: "/home/guide.html", label: "Hướng dẫn" },
    ];

    if (isAuthed) {
      return baseLinks.concat([
        { href: "/home/dashboard.html", label: "Tổng quan" },
        { href: "/home/profile.html", label: "Hồ sơ" },
      ]);
    }

    return baseLinks.concat([
      { href: "/home/login.html", label: "Đăng nhập" },
      { href: "/home/register.html", label: "Đăng ký" },
    ]);
  }

  function getPublicNavLinks() {
    return [
      { href: "/home/index.html", label: "Trang chủ" },
      { href: "/home/about.html", label: "Giới thiệu" },
      { href: "/home/guide.html", label: "Hướng dẫn" },
    ];
  }

  function initPublicDrawer() {
    const headerContainer = document.querySelector(".public-header .container");
    if (!headerContainer || document.getElementById("publicDrawer")) {
      return;
    }

    const actions = headerContainer.querySelector(".header-actions");
    const brand = headerContainer.querySelector(".brand");
    const existingNav = headerContainer.querySelector(".public-nav");
    let headerMain = headerContainer.querySelector(".public-header-main");

    if (!headerMain) {
      headerMain = document.createElement("div");
      headerMain.className = "public-header-main";
      headerContainer.insertBefore(headerMain, actions || headerContainer.firstChild);
    }

    const menuButton = document.createElement("button");
    menuButton.type = "button";
    menuButton.className = "icon-button public-menu-toggle";
    menuButton.id = "publicMenuToggle";
    menuButton.setAttribute("aria-label", "Mo menu dieu huong");
    menuButton.setAttribute("aria-expanded", "false");
    menuButton.innerHTML = `
      <span class="hamburger-lines" aria-hidden="true">
        <span></span>
        <span></span>
        <span></span>
      </span>
      <span class="public-menu-label">Menu</span>`;

    const currentPath = window.location.pathname.toLowerCase();
    const nav = existingNav || document.createElement("nav");
    nav.className = "public-nav";
    nav.innerHTML = getPublicNavLinks()
      .map(
        (link) => `
          <a class="${currentPath === link.href.toLowerCase() ? "is-active" : ""}" href="${link.href}">
            ${escapeHtml(link.label)}
          </a>`
      )
      .join("");

    headerMain.appendChild(menuButton);
    if (brand) {
      headerMain.appendChild(brand);
    }
    headerMain.appendChild(nav);

    const currentUser = window.AuthClient?.getCurrentUser?.();
    const links = getPublicDrawerLinks();
    const drawer = document.createElement("aside");
    drawer.className = "public-drawer";
    drawer.id = "publicDrawer";
    drawer.innerHTML = `
      <div class="public-drawer-card">
        <div class="public-drawer-header">
          <div>
            <div class="card-kicker">Navigation</div>
            <strong>${isTruthy(currentUser?.fullName) ? escapeHtml(currentUser.fullName) : "SmartSpend AI"}</strong>
            <div class="muted">${escapeHtml(currentUser?.email || "Khung menu dùng chung cho các trang public/auth.")}</div>
          </div>
          <button class="icon-button" type="button" id="publicDrawerClose" aria-label="Dong menu">X</button>
        </div>
        <div class="sidebar-title">Điều hướng chính</div>
        <nav class="public-drawer-nav">
          ${links
            .map(
              (link) => `
                <a class="public-drawer-link ${currentPath === link.href.toLowerCase() ? "is-active" : ""}" href="${link.href}">
                  ${escapeHtml(link.label)}
                </a>`
            )
            .join("")}
        </nav>
        <div class="sidebar-title">Tài khoản</div>
        <div class="public-drawer-actions">
          ${
            currentUser
              ? `
                <a class="btn btn-primary" href="/home/dashboard.html">Vào dashboard</a>
                <a class="btn btn-secondary" href="/home/login.html" data-public-logout>Đăng xuất</a>`
              : `
                <a class="btn btn-primary" href="/home/register.html">Tạo tài khoản</a>
                <a class="btn btn-secondary" href="/home/login.html">Đăng nhập</a>`
          }
        </div>
      </div>`;

    const backdrop = document.createElement("div");
    backdrop.className = "public-drawer-backdrop";
    backdrop.id = "publicDrawerBackdrop";

    document.body.appendChild(drawer);
    document.body.appendChild(backdrop);

    const closeButton = drawer.querySelector("#publicDrawerClose");
    const logoutButton = drawer.querySelector("[data-public-logout]");
    const closeDrawer = () => {
      drawer.classList.remove("is-open");
      backdrop.classList.remove("is-visible");
      menuButton.setAttribute("aria-expanded", "false");
    };
    const openDrawer = () => {
      drawer.classList.add("is-open");
      backdrop.classList.add("is-visible");
      menuButton.setAttribute("aria-expanded", "true");
    };

    menuButton.addEventListener("click", () => {
      if (drawer.classList.contains("is-open")) {
        closeDrawer();
      } else {
        openDrawer();
      }
    });

    closeButton?.addEventListener("click", closeDrawer);
    backdrop.addEventListener("click", closeDrawer);
    drawer.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => {
        closeDrawer();
      });
    });

    logoutButton?.addEventListener("click", async (event) => {
      event.preventDefault();
      await window.AuthClient?.logout?.();
      window.location.href = "/home/login.html";
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && drawer.classList.contains("is-open")) {
        closeDrawer();
      }
    });
  }

  function isTruthy(value) {
    return Boolean(String(value || "").trim());
  }

  window.SmartSpendApp = {
    api,
    extractErrorMessage,
    escapeHtml,
    formatCurrency,
    formatDate,
    formatDateInput,
    formatDateTime,
    showToast,
    refreshAlerts,
    get currentUser() {
      return state.currentUser;
    },
  };

  document.addEventListener("DOMContentLoaded", async () => {
    setCurrentYear();
    initTheme();

    if ((document.body.dataset.layout || "public") === "app") {
      await ensureProtectedLayout();
    } else {
      initPublicPage();
    }
  });
})();

