(function () {
  const tokenStorageKey = "auth.accessToken";
  const userStorageKey = "auth.currentUser";
  const loginPage = "login.html";

  const readFromStorages = (key) => {
    const localValue = window.localStorage.getItem(key);
    if (localValue) {
      return { value: localValue, storage: window.localStorage };
    }

    const sessionValue = window.sessionStorage.getItem(key);
    if (sessionValue) {
      return { value: sessionValue, storage: window.sessionStorage };
    }

    return { value: null, storage: null };
  };

  const getAccessToken = () => readFromStorages(tokenStorageKey).value;

  const normalizeRole = (role) => {
    const value = String(role || "").toLowerCase();
    if (value === "systemadmin" || value === "admin") {
      return "Quản trị viên";
    }

    if (value === "standarduser" || value === "user") {
      return "Người dùng";
    }

    return "Khách";
  };

  const getCurrentUser = () => {
    const raw = readFromStorages(userStorageKey).value;
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  };

  const clearSession = () => {
    window.localStorage.removeItem(tokenStorageKey);
    window.localStorage.removeItem(userStorageKey);
    window.sessionStorage.removeItem(tokenStorageKey);
    window.sessionStorage.removeItem(userStorageKey);
  };

  const setCurrentUser = (user, rememberMe) => {
    const payload = JSON.stringify(user || {});
    const storage = rememberMe ? window.localStorage : window.sessionStorage;
    storage.setItem(userStorageKey, payload);
  };

  const isAuthenticated = () => Boolean(getAccessToken());

  const setElementVisible = (element, visible) => {
    if (!element) {
      return;
    }

    if (visible) {
      element.style.removeProperty("display");
      return;
    }

    element.style.setProperty("display", "none", "important");
  };

  const applyAuthVisibility = () => {
    const authed = isAuthenticated();

    document.querySelectorAll("[data-auth-guest]").forEach((el) => {
      setElementVisible(el, !authed);
    });

    document.querySelectorAll("[data-auth-user]").forEach((el) => {
      setElementVisible(el, authed);
    });
  };

  const redirectToLogin = (message) => {
    const current = `${window.location.pathname}${window.location.search}`;
    const returnUrl = encodeURIComponent(current);
    const hint = message ? `&message=${encodeURIComponent(message)}` : "";
    window.location.href = `${loginPage}?returnUrl=${returnUrl}${hint}`;
  };

  const apiGetMe = async () => {
    const token = getAccessToken();
    if (!token) {
      return { ok: false, status: 401, data: null };
    }

    try {
      const response = await fetch("/api/auth/me", {
        method: "GET",
        headers: { Authorization: `Bearer ${token}` },
      });

      const contentType = response.headers.get("content-type") || "";
      const data = contentType.toLowerCase().includes("application/json")
        ? await response.json().catch(() => null)
        : null;

      return { ok: response.ok, status: response.status, data };
    } catch {
      return { ok: false, status: 0, data: null };
    }
  };

  const requireAuth = async (options) => {
    const opts = options || {};
    const requiredRoles = Array.isArray(opts.roles) ? opts.roles : [];
    const normalizedRequiredRoles = requiredRoles
      .map((item) => normalizeRole(item))
      .filter((item, index, array) => item !== "Khách" && array.indexOf(item) === index);
    const onForbidden =
      typeof opts.onForbidden === "function" ? opts.onForbidden : null;

    if (!isAuthenticated()) {
      redirectToLogin("Vui lòng đăng nhập để tiếp tục.");
      return null;
    }

    const me = await apiGetMe();
    if (!me.ok || !me.data) {
      clearSession();
      redirectToLogin("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
      return null;
    }

    const normalizedRole = normalizeRole(me.data.role);
    if (normalizedRequiredRoles.length > 0 && !normalizedRequiredRoles.includes(normalizedRole)) {
      if (onForbidden) {
        onForbidden(me.data);
        return null;
      }

      window.location.href = "/home/index.html";
      return null;
    }

    return me.data;
  };

  const getInitials = (name, email) => {
    const source = String(name || email || "").trim();
    if (!source) {
      return "ND";
    }

    const parts = source.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    }

    return source.slice(0, 2).toUpperCase();
  };

  const bindUserUi = (me, options) => {
    const opts = options || {};
    const nameSelector = opts.nameSelector || "[data-auth-name]";
    const avatarSelector = opts.avatarSelector || "[data-auth-avatar]";
    const roleSelector = opts.roleSelector || "[data-auth-role]";
    const logoutSelector = opts.logoutSelector || "[data-auth-logout]";

    document.querySelectorAll(nameSelector).forEach((el) => {
      el.textContent = me.fullName || me.username || me.email || "Người dùng";
    });

    document.querySelectorAll(avatarSelector).forEach((el) => {
      el.textContent = getInitials(me.fullName || me.username, me.email);
    });

    document.querySelectorAll(roleSelector).forEach((el) => {
      el.textContent = normalizeRole(me.role);
    });

    document.querySelectorAll(logoutSelector).forEach((el) => {
      el.addEventListener("click", (event) => {
        event.preventDefault();
        clearSession();
        window.location.href = loginPage;
      });
    });
  };

  const logout = async () => {
    clearSession();
  };

  window.AuthClient = {
    getAccessToken,
    getCurrentUser,
    setCurrentUser,
    clearSession,
    logout,
    isAuthenticated,
    requireAuth,
    applyAuthVisibility,
    bindUserUi,
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", applyAuthVisibility, { once: true });
  } else {
    applyAuthVisibility();
  }
})();
