(function () {
  const appReadyHandlers = {
    dashboard: initDashboardPage,
    wallets: initWalletsPage,
    transactions: initTransactionsPage,
    budgets: initBudgetsPage,
    reports: initReportsPage,
    profile: initProfilePage,
    "admin-dashboard": initAdminDashboardPage,
    "admin-users": initAdminUsersPage,
    "admin-categories": initAdminCategoriesPage,
    "admin-logs": initAdminLogsPage,
  };
  let trendChartInstance = null;
  let expenseChartInstance = null;

  function message(targetId, text, tone) {
    const element = document.getElementById(targetId);
    if (!element) {
      return;
    }

    if (!text) {
      element.hidden = true;
      element.textContent = "";
      element.className = "message";
      return;
    }

    element.hidden = false;
    element.textContent = text;
    element.className = `message ${tone || ""}`.trim();
  }

  function budgetTone(status) {
    const value = String(status || "").toLowerCase();
    if (value.includes("danger")) {
      return "danger";
    }

    if (value.includes("warning")) {
      return "warning";
    }

    return "safe";
  }

  function statusBadge(status) {
    const tone = budgetTone(status);
    const labelMap = {
      safe: "An toàn",
      warning: "Cảnh báo",
      danger: "Vượt mức",
      info: "Thông tin",
    };
    return `<span class="status-pill status-${tone}">${window.SmartSpendApp.escapeHtml(labelMap[tone] || status || "An toàn")}</span>`;
  }

  function renderTrendChart(points) {
    const shell = document.getElementById("trendChartShell");
    if (!shell) {
      return;
    }

    if (trendChartInstance) {
      trendChartInstance.destroy();
      trendChartInstance = null;
    }

    if (!Array.isArray(points) || points.length === 0) {
      shell.innerHTML = '<div class="empty-state">Chưa có dữ liệu đủ để dựng biểu đồ thu - chi.</div>';
      return;
    }

    if (!window.Chart) {
      shell.innerHTML = '<div class="empty-state">Không thể tải Chart.js. Vui lòng thử lại.</div>';
      return;
    }

    shell.innerHTML = '<canvas id=\"trendChart\" aria-label=\"Biểu đồ thu chi theo tháng\"></canvas>';
    const canvas = document.getElementById("trendChart");
    if (!canvas) {
      return;
    }

    trendChartInstance = new window.Chart(canvas, {
      type: "bar",
      data: {
        labels: points.map((item) => item.label || ""),
        datasets: [
          {
            label: "Thu",
            data: points.map((item) => Number(item.income || 0)),
            backgroundColor: "#16A34A",
            borderRadius: 10,
            maxBarThickness: 36,
          },
          {
            label: "Chi",
            data: points.map((item) => Number(item.expense || 0)),
            backgroundColor: "#DC2626",
            borderRadius: 10,
            maxBarThickness: 36,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            position: "top",
          },
          tooltip: {
            callbacks: {
              label(context) {
                return `${context.dataset.label}: ${window.SmartSpendApp.formatCurrency(context.parsed.y)}`;
              },
            },
          },
        },
        scales: {
          x: {
            grid: {
              display: false,
            },
          },
          y: {
            beginAtZero: true,
            ticks: {
              callback(value) {
                return window.SmartSpendApp.formatCurrency(value);
              },
            },
          },
        },
      },
    });
  }

  function normalizeRoleLabel(role) {
    const value = String(role || "").toLowerCase();
    if (value === "systemadmin" || value === "admin") {
      return "Quản trị viên";
    }

    if (value === "standarduser" || value === "user") {
      return "Người dùng";
    }

    return "Khách";
  }

  function renderExpenseDonut(items) {
    const shell = document.getElementById("expenseDonutShell");
    const legend = document.getElementById("expenseLegend");
    if (!shell || !legend) {
      return;
    }

    if (expenseChartInstance) {
      expenseChartInstance.destroy();
      expenseChartInstance = null;
    }

    if (!Array.isArray(items) || items.length === 0) {
      shell.innerHTML = '<div class="empty-state">Chưa có giao dịch chi tiêu trong tháng.</div>';
      legend.innerHTML = '<div class="empty-state">Chưa có giao dịch chi tiêu trong tháng.</div>';
      return;
    }

    if (!window.Chart) {
      shell.innerHTML = '<div class="empty-state">Không thể tải Chart.js. Vui lòng thử lại.</div>';
      return;
    }

    shell.innerHTML = '<canvas id=\"expenseDonut\" aria-label=\"Biểu đồ cơ cấu chi tiêu\"></canvas>';
    const canvas = document.getElementById("expenseDonut");
    if (!canvas) {
      return;
    }

    expenseChartInstance = new window.Chart(canvas, {
      type: "pie",
      data: {
        labels: items.map((item) => item.categoryName || "Khac"),
        datasets: [
          {
            data: items.map((item) => Number(item.amount || 0)),
            backgroundColor: items.map((item) => item.color || "#2563EB"),
            borderWidth: 0,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            display: false,
          },
          tooltip: {
            callbacks: {
              label(context) {
                return `${context.label}: ${window.SmartSpendApp.formatCurrency(context.parsed)}`;
              },
            },
          },
        },
      },
    });

    legend.innerHTML = items
      .map(
        (item) => `
          <div class="legend-item">
            <div class="inline-actions">
              <span class="legend-swatch" style="background:${window.SmartSpendApp.escapeHtml(item.color || "#48d1a0")}"></span>
              <span>${window.SmartSpendApp.escapeHtml(item.categoryName)}</span>
            </div>
            <strong>${window.SmartSpendApp.formatCurrency(item.amount)}</strong>
          </div>`
      )
      .join("");
  }

  function renderTransactionsTimeline(transactions) {
    const container = document.getElementById("transactionTimeline");
    if (!container) {
      return;
    }

    if (!Array.isArray(transactions) || transactions.length === 0) {
      container.innerHTML = '<div class="empty-state">Chưa có giao dịch để hiển thị dạng timeline.</div>';
      return;
    }

    container.innerHTML = transactions
      .map((item) => {
        const tone = item.type === "Income" ? "income" : "expense";
        return `
          <article class="timeline-item ${tone}">
            <div class="timeline-dot"></div>
            <div class="timeline-content">
              <div class="timeline-top">
                <strong>${window.SmartSpendApp.escapeHtml(item.note || "Không có ghi chú")}</strong>
                <span class="status-pill status-${tone === "income" ? "safe" : "danger"}">${item.type === "Income" ? "Thu" : "Chi"}</span>
              </div>
              <div class="muted">${window.SmartSpendApp.formatDate(item.transactionDate)} | ${window.SmartSpendApp.escapeHtml(item.walletName)} | ${window.SmartSpendApp.escapeHtml(item.categoryName)}</div>
              <div class="timeline-amount ${tone}">${window.SmartSpendApp.formatCurrency(item.amount)}</div>
            </div>
          </article>`;
      })
      .join("");
  }

  function renderBudgetProgress(items, targetId) {
    const container = document.getElementById(targetId);
    if (!container) {
      return;
    }

    if (!Array.isArray(items) || items.length === 0) {
      container.innerHTML = '<div class="empty-state">Chưa thiết lập ngân sách nào trong tháng này.</div>';
      return;
    }

    container.innerHTML = items
      .map((item) => {
        const tone = budgetTone(item.status);
        const percentage = Math.min(Number(item.progressPercentage || 0), 100);
        return `
          <div class="budget-row">
            <div class="panel-header" style="margin:0;">
              <div>
                <strong>${window.SmartSpendApp.escapeHtml(item.categoryName)}</strong>
                <div class="muted">Đã dùng ${window.SmartSpendApp.formatCurrency(item.spentAmount)} / ${window.SmartSpendApp.formatCurrency(item.limitAmount)}</div>
              </div>
              ${statusBadge(item.status)}
            </div>
            <div class="progress-track"><div class="progress-fill ${tone}" style="width:${percentage}%;"></div></div>
          </div>`;
      })
      .join("");
  }

  function renderBulletList(targetId, items, emptyText) {
    const container = document.getElementById(targetId);
    if (!container) {
      return;
    }

    if (!Array.isArray(items) || items.length === 0) {
      container.innerHTML = `<div class="empty-state">${window.SmartSpendApp.escapeHtml(emptyText || "Chưa có dữ liệu.")}</div>`;
      return;
    }

    const toText = (item) => {
      if (typeof item === "string") {
        return item;
      }

      if (item && typeof item === "object") {
        return item.message || item.Message || item.type || item.Type || "";
      }

      return String(item || "");
    };

    container.innerHTML = `<ul class="insight-list">${items
      .map((item) => `<li>${window.SmartSpendApp.escapeHtml(toText(item))}</li>`)
      .join("")}</ul>`;
  }

  function renderForecastSummary(summary) {
    const container = document.getElementById("forecastSummaryPanel");
    if (!container) {
      return;
    }

    if (!summary) {
      container.innerHTML = '<div class="empty-state">Chưa có dữ liệu dự báo AI.</div>';
      return;
    }

    const fixedItems = Array.isArray(summary.fixedCostItems) ? summary.fixedCostItems : [];
    const explanations = Array.isArray(summary.explanations) ? summary.explanations : [];

    const fixedItemsHtml =
      fixedItems.length === 0
        ? '<div class="empty-state">AI chưa nhận diện khoản chi cố định nào trong 2-3 tháng gần đây.</div>'
        : `<div class="legend-list">${fixedItems
            .map(
              (item) => `
                <div class="legend-item">
                  <div>
                    <strong>${window.SmartSpendApp.escapeHtml(item.label || "Khoản chi cố định")}</strong>
                    <div class="muted">${
                      item.alreadyCharged
                        ? "Đã ghi nhận trong tháng này"
                        : `Dự kiến quanh ngày ${item.expectedDayOfMonth || "--"}`
                    }</div>
                  </div>
                  <strong>${window.SmartSpendApp.formatCurrency(item.amount || 0)}</strong>
                </div>`
            )
            .join("")}</div>`;

    container.innerHTML = `
      <div class="metrics-grid" style="margin-bottom:12px;">
        <article class="metric-card">
          <div class="metric-label">Cố định đã chi</div>
          <div class="metric-value">${window.SmartSpendApp.formatCurrency(summary.fixedSpentThisMonth || 0)}</div>
        </article>
        <article class="metric-card">
          <div class="metric-label">Cố định còn lại</div>
          <div class="metric-value">${window.SmartSpendApp.formatCurrency(summary.fixedRemainingThisMonth || 0)}</div>
        </article>
        <article class="metric-card">
          <div class="metric-label">Linh hoạt còn lại</div>
          <div class="metric-value">${window.SmartSpendApp.formatCurrency(summary.flexibleProjectedRemaining || 0)}</div>
        </article>
        <article class="metric-card">
          <div class="metric-label">Số dư cuối tháng dự kiến</div>
          <div class="metric-value">${window.SmartSpendApp.formatCurrency(summary.projectedEndBalance || 0)}</div>
        </article>
      </div>
      <div class="stack">
        <div>
          <h3 style="margin-bottom:8px;">Vì sao AI dự đoán như vậy?</h3>
          ${explanations.length
            ? `<ul class="insight-list">${explanations
                .map((item) => `<li>${window.SmartSpendApp.escapeHtml(item)}</li>`)
                .join("")}</ul>`
            : '<div class="empty-state">Chưa có giải thích AI.</div>'}
        </div>
        <div>
          <h3 style="margin-bottom:8px;">Khoản chi cố định nhận diện được</h3>
          ${fixedItemsHtml}
        </div>
      </div>`;
  }

  function renderAiReminders(items) {
    const anomalyContainer = document.getElementById("aiAnomalyList");
    const warningContainer = document.getElementById("aiWarningList");
    const recommendationContainer = document.getElementById("aiRecommendationList");
    if (!anomalyContainer || !warningContainer || !recommendationContainer) {
      return;
    }

    const safeItems = Array.isArray(items) ? items : [];

    const anomalyItems = safeItems.filter(
      (item) => item.type === "Anomaly" || item.algorithm === "StatisticalAnomalyDetection"
    );
    const recommendationItems = safeItems.filter(
      (item) => item.type === "Recommendation" || item.algorithm === "TopKRecommendation"
    );
    const warningItems = safeItems.filter(
      (item) =>
        !anomalyItems.includes(item) &&
        !recommendationItems.includes(item) &&
        (item.level === "Danger" ||
          item.level === "Warning" ||
          item.type === "Budget" ||
          item.type === "Forecast" ||
          item.type === "Spike" ||
          item.type === "Trend")
    );

    function compactText(value, maxLength) {
      const text = String(value || "").trim();
      if (!text) {
        return "";
      }

      if (text.length <= maxLength) {
        return text;
      }

      return `${text.slice(0, maxLength).trim()}...`;
    }

    function algorithmLabel(value) {
      const map = {
        StatisticalAnomalyDetection: "Anomaly",
        TopKRecommendation: "Top-K",
        WeightedMovingAverage: "WMA",
        BudgetThreshold: "Budget",
        MonthlyTrend: "Trend",
        WeeklyTrend: "Week",
        BudgetPace: "Pace",
        OutlierSpike: "Spike",
      };

      return map[value] || compactText(value, 16);
    }

    function renderReminderGroup(container, groupItems, emptyText) {
      if (!groupItems.length) {
        container.innerHTML = `<div class="empty-state">${window.SmartSpendApp.escapeHtml(emptyText)}</div>`;
        return;
      }

      const prioritizedItems = groupItems.slice(0, 2);
      container.innerHTML = `<div class="ai-reminder-stack">${prioritizedItems
        .map((item) => {
        const tone = budgetTone(item.level || "info");
        const explanation = item.explanation
          ? `<div class="ai-reminder-note">${window.SmartSpendApp.escapeHtml(compactText(item.explanation, 88))}</div>`
          : "";
        const action = item.suggestedAction
          ? `<div class="ai-reminder-action">${window.SmartSpendApp.escapeHtml(compactText(item.suggestedAction, 72))}</div>`
          : "";
        const metaParts = [
          item.algorithm ? algorithmLabel(item.algorithm) : "",
          item.categoryName || "",
        ].filter(Boolean);

        return `
          <article class="ai-reminder-card">
            <div class="ai-reminder-top">
              <div class="ai-reminder-copy">
                <strong class="ai-reminder-title">${window.SmartSpendApp.escapeHtml(compactText(item.message || "Nhắc nhở AI", 68))}</strong>
                ${metaParts.length ? `<div class="ai-reminder-meta">${window.SmartSpendApp.escapeHtml(metaParts.join(" • "))}</div>` : ""}
              </div>
              <span class="status-pill status-${tone}">${window.SmartSpendApp.escapeHtml(({ Danger: "Vượt mức", Warning: "Cảnh báo", Info: "Thông tin" }[item.level] || item.level || "Thông tin"))}</span>
            </div>
            ${explanation}
            ${action}
          </article>`;
        })
        .join("")}</div>`;
    }

    renderReminderGroup(
      anomalyContainer,
      anomalyItems,
      "Chưa có bất thường nào đang được AI phát hiện."
    );
    renderReminderGroup(
      warningContainer,
      warningItems,
      "Chưa có cảnh báo chi tiêu nào cần ưu tiên lúc này."
    );
    renderReminderGroup(
      recommendationContainer,
      recommendationItems,
      "Chưa có gợi ý cắt giảm nào đủ mạnh để hiển thị."
    );
  }

  async function initDashboardPage() {
    try {
      const data = await window.SmartSpendApp.api("/api/dashboard");
      const mappings = {
        totalBalance: data.totalBalance,
        totalIncome: data.totalIncomeThisMonth,
        totalExpense: data.totalExpenseThisMonth,
      };

      Object.entries(mappings).forEach(([id, value]) => {
        const node = document.getElementById(id);
        if (node) {
          node.textContent = window.SmartSpendApp.formatCurrency(value);
        }
      });

      const alertsNode = document.getElementById("dashboardAlertCount");
      if (alertsNode) {
        alertsNode.textContent = String(data.unreadAlerts || 0);
      }

      renderTrendChart(data.monthlyTrend || []);
      renderExpenseDonut(data.expenseBreakdown || []);
      renderBudgetProgress(data.budgetProgress || [], "dashboardBudgets");
      renderBulletList("insightsList", data.insights || [], "Chưa có insight nào.");
      renderBulletList("forecastList", data.forecasts || [], "Chưa có dự báo nào.");
      renderForecastSummary(data.forecastSummary || null);
      renderAiReminders(data.aiReminders || []);
    } catch (error) {
      message("dashboardMessage", error.message, "error");
    }
  }

  async function initWalletsPage() {
    const walletForm = document.getElementById("walletForm");
    const transferForm = document.getElementById("transferForm");
    const walletList = document.getElementById("walletList");
    const walletNameInput = document.getElementById("walletName");
    const walletTypeSelect = document.getElementById("walletType");
    const walletInitialBalanceInput = document.getElementById("walletInitialBalance");
    const walletIsDefaultInput = document.getElementById("walletIsDefault");
    const fromWalletSelect = document.getElementById("fromWalletId");
    const toWalletSelect = document.getElementById("toWalletId");
    const transferAmountInput = document.getElementById("transferAmount");
    const transferDateInput = document.getElementById("transferDate");
    const transferNoteInput = document.getElementById("transferNote");
    let editingWalletId = null;
    let wallets = [];

    function updateTransferOptions() {
      if (!fromWalletSelect || !toWalletSelect) {
        return;
      }

      const options = ['<option value="">Chọn ví</option>']
        .concat(
          wallets.map(
            (item) => `<option value="${item.walletId}">${window.SmartSpendApp.escapeHtml(item.name)} (${window.SmartSpendApp.formatCurrency(item.balance)})</option>`
          )
        )
        .join("");

      fromWalletSelect.innerHTML = options;
      toWalletSelect.innerHTML = options;
    }

    function resetWalletForm() {
      editingWalletId = null;
      walletForm?.reset();
      const submit = walletForm?.querySelector("button[type='submit']");
      if (submit) {
        submit.textContent = "Lưu ví";
      }
      const cancel = document.getElementById("cancelWalletEdit");
      if (cancel) {
        cancel.hidden = true;
      }
    }

    async function loadWallets() {
      wallets = await window.SmartSpendApp.api("/api/wallets");
      updateTransferOptions();

      const totalNode = document.getElementById("walletTotalBalance");
      if (totalNode) {
        const total = wallets.reduce((sum, item) => sum + Number(item.balance || 0), 0);
        totalNode.textContent = window.SmartSpendApp.formatCurrency(total);
      }

      const countNode = document.getElementById("walletCount");
      if (countNode) {
        countNode.textContent = String(wallets.length);
      }

      if (!walletList) {
        return;
      }

      if (wallets.length === 0) {
        walletList.innerHTML = '<div class="empty-state">Chưa có ví nào. Tạo ví đầu tiên để bắt đầu ghi nhận chi tiêu.</div>';
        return;
      }

      walletList.innerHTML = wallets
        .map(
          (wallet) => `
            <article class="wallet-card">
              <div class="panel-header" style="margin-bottom:8px;">
                <div>
                  <div class="wallet-badge">${wallet.isDefault ? "★" : wallet.type.slice(0, 1)}</div>
                  <h3>${window.SmartSpendApp.escapeHtml(wallet.name)}</h3>
                </div>
                ${wallet.isDefault ? '<span class="tag tag-safe">Mặc định</span>' : ''}
              </div>
              <p class="muted">Loại ví: ${window.SmartSpendApp.escapeHtml(wallet.type)}</p>
              <div class="metric-value">${window.SmartSpendApp.formatCurrency(wallet.balance)}</div>
              <div class="muted">Tạo ngày ${window.SmartSpendApp.formatDate(wallet.createdAt)}</div>
              <div class="table-actions">
                <button class="btn btn-secondary" data-edit-wallet="${wallet.walletId}">Sửa</button>
                <button class="btn btn-danger" data-delete-wallet="${wallet.walletId}">Xóa</button>
              </div>
            </article>`
        )
        .join("");

      walletList.querySelectorAll("[data-edit-wallet]").forEach((button) => {
        button.addEventListener("click", () => {
          const wallet = wallets.find((item) => item.walletId === Number(button.dataset.editWallet));
          if (!wallet || !walletForm) {
            return;
          }

          editingWalletId = wallet.walletId;
          walletNameInput.value = wallet.name;
          walletTypeSelect.value = wallet.type;
          walletInitialBalanceInput.value = wallet.balance;
          walletIsDefaultInput.checked = Boolean(wallet.isDefault);
          walletForm.querySelector("button[type='submit']").textContent = "Cập nhật ví";
          document.getElementById("cancelWalletEdit").hidden = false;
          window.scrollTo({ top: 0, behavior: "smooth" });
        });
      });

      walletList.querySelectorAll("[data-delete-wallet]").forEach((button) => {
        button.addEventListener("click", async () => {
          if (!window.confirm("Bạn có chắc muốn xóa ví này?")) {
            return;
          }

          try {
            await window.SmartSpendApp.api(`/api/wallets/${button.dataset.deleteWallet}`, { method: "DELETE" });
            window.SmartSpendApp.showToast("Đã xóa ví.", "success");
            await loadWallets();
            resetWalletForm();
          } catch (error) {
            message("walletMessage", error.message, "error");
          }
        });
      });
    }

    walletForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("walletMessage", "", "");
      try {
        const payload = {
          name: walletNameInput.value,
          type: walletTypeSelect.value,
          initialBalance: Number(walletInitialBalanceInput.value || 0),
          isDefault: walletIsDefaultInput.checked,
        };

        await window.SmartSpendApp.api(editingWalletId ? `/api/wallets/${editingWalletId}` : "/api/wallets", {
          method: editingWalletId ? "PUT" : "POST",
          body: payload,
        });

        window.SmartSpendApp.showToast(editingWalletId ? "Đã cập nhật ví." : "Đã tạo ví mới.", "success");
        resetWalletForm();
        await loadWallets();
      } catch (error) {
        message("walletMessage", error.message, "error");
      }
    });

    transferForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("transferMessage", "", "");
      try {
        await window.SmartSpendApp.api("/api/wallets/transfer", {
          method: "POST",
          body: {
            fromWalletId: Number(fromWalletSelect.value),
            toWalletId: Number(toWalletSelect.value),
            amount: Number(transferAmountInput.value),
            note: transferNoteInput.value,
            transferDate: transferDateInput.value ? `${transferDateInput.value}T00:00:00Z` : null,
          },
        });

        transferForm.reset();
        message("transferMessage", "Chuyển tiền thành công.", "success");
        await loadWallets();
      } catch (error) {
        message("transferMessage", error.message, "error");
      }
    });

    document.getElementById("cancelWalletEdit")?.addEventListener("click", resetWalletForm);
    await loadWallets();
  }

  async function initTransactionsPage() {
    const transactionForm = document.getElementById("transactionForm");
    const filterForm = document.getElementById("transactionFilterForm");
    const tableBody = document.getElementById("transactionTableBody");
    const smartInputButton = document.getElementById("smartInputSuggestButton");
    const smartInputSuggestion = document.getElementById("smartInputSuggestion");
    let wallets = [];
    let categories = [];
    let transactions = [];
    let editingTransactionId = null;

    function renderSmartInputSuggestion(result) {
      if (!smartInputSuggestion) {
        return;
      }

      if (!result) {
        smartInputSuggestion.innerHTML = "Chưa có gợi ý AI.";
        return;
      }

      const lines = [];
      if (result.suggestedCategoryName) {
        lines.push(`Danh mục: ${result.suggestedCategoryName}`);
      }
      if (result.suggestedType) {
        lines.push(`Loại: ${result.suggestedType === "Income" ? "Thu" : "Chi"}`);
      }
      if (result.suggestedWalletName) {
        lines.push(`Ví: ${result.suggestedWalletName}`);
      }
      if (result.amount) {
        lines.push(`Số tiền AI nhận ra: ${window.SmartSpendApp.formatCurrency(result.amount)}`);
      }
      lines.push(`Độ tin cậy: ${Math.round(Number(result.aiConfidence || 0) * 100)}%`);

      const reasons = Array.isArray(result.reasoning) ? result.reasoning : [];
      smartInputSuggestion.innerHTML = `
        <div><strong>${window.SmartSpendApp.escapeHtml(lines.join(" | "))}</strong></div>
        ${
          reasons.length
            ? `<ul class="insight-list">${reasons
                .map((item) => `<li>${window.SmartSpendApp.escapeHtml(item)}</li>`)
                .join("")}</ul>`
            : ""
        }`;
    }

    function setSelectOptions(selectId, items, placeholder) {
      const select = document.getElementById(selectId);
      if (!select) {
        return;
      }

      select.innerHTML = [`<option value="">${placeholder}</option>`]
        .concat(items.map((item) => `<option value="${item.walletId || item.categoryId}">${window.SmartSpendApp.escapeHtml(item.name || item.categoryName)}</option>`))
        .join("");
    }

    async function loadLookups() {
      wallets = await window.SmartSpendApp.api("/api/wallets");
      categories = await window.SmartSpendApp.api("/api/categories");

      setSelectOptions("walletId", wallets, "Chọn ví");
      setSelectOptions("filterWalletId", wallets, "Tất cả ví");
      setSelectOptions("categoryId", categories, "Chọn danh mục");
      setSelectOptions("filterCategoryId", categories, "Tất cả danh mục");

      if (wallets.length > 0 && transactionForm && !transactionForm.walletId.value) {
        const defaultWallet = wallets.find((item) => item.isDefault) || wallets[0];
        transactionForm.walletId.value = String(defaultWallet.walletId);
      }
    }

    function resetTransactionForm() {
      editingTransactionId = null;
      transactionForm?.reset();
      transactionForm.transactionDate.value = new Date().toISOString().slice(0, 10);
      transactionForm.querySelector("button[type='submit']").textContent = "Lưu giao dịch";
      document.getElementById("cancelTransactionEdit").hidden = true;
      renderSmartInputSuggestion(null);
      if (wallets.length > 0) {
        const defaultWallet = wallets.find((item) => item.isDefault) || wallets[0];
        transactionForm.walletId.value = String(defaultWallet.walletId);
      }
    }

    async function applySmartInputSuggestion() {
      if (!transactionForm) {
        return;
      }

      const note = String(transactionForm.note.value || "").trim();
      const amount = String(transactionForm.amount.value || "").trim();
      const combinedInput = [note, amount].filter(Boolean).join(" ");

      if (!combinedInput) {
        renderSmartInputSuggestion({
          reasoning: ["Hãy nhập ghi chú hoặc số tiền để AI có thể gợi ý."],
          aiConfidence: 0,
        });
        return;
      }

      const result = await window.SmartSpendApp.api("/api/ai/smart-input", {
        method: "POST",
        body: {
          input: combinedInput,
        },
      });

      if (result.suggestedCategoryId) {
        transactionForm.categoryId.value = String(result.suggestedCategoryId);
      }
      if (result.suggestedWalletId) {
        transactionForm.walletId.value = String(result.suggestedWalletId);
      }
      if (result.suggestedType) {
        transactionForm.type.value = result.suggestedType;
      }
      if ((!transactionForm.amount.value || Number(transactionForm.amount.value) <= 0) && Number(result.amount || 0) > 0) {
        transactionForm.amount.value = String(Number(result.amount));
      }
      if (!transactionForm.transactionDate.value && result.transactionDate) {
        transactionForm.transactionDate.value = window.SmartSpendApp.formatDateInput(result.transactionDate);
      }

      renderSmartInputSuggestion(result);
    }

    function buildQueryFromFilters() {
      const params = new URLSearchParams();
      if (filterForm.from.value) params.set("from", filterForm.from.value);
      if (filterForm.to.value) params.set("to", filterForm.to.value);
      if (filterForm.walletId.value) params.set("walletId", filterForm.walletId.value);
      if (filterForm.categoryId.value) params.set("categoryId", filterForm.categoryId.value);
      if (filterForm.type.value) params.set("type", filterForm.type.value);
      return params.toString();
    }

    async function loadTransactions() {
      const query = filterForm ? buildQueryFromFilters() : "";
      transactions = await window.SmartSpendApp.api(`/api/transactions${query ? `?${query}` : ""}`);
      const totalNode = document.getElementById("transactionCount");
      if (totalNode) {
        totalNode.textContent = String(transactions.length);
      }
      renderTransactionsTimeline(transactions);

      if (!tableBody) {
        return;
      }

      if (transactions.length === 0) {
        tableBody.innerHTML = '<tr><td colspan="7"><div class="empty-state">Chưa có giao dịch phù hợp bộ lọc hiện tại.</div></td></tr>';
        return;
      }

      tableBody.innerHTML = transactions
        .map(
          (item) => `
            <tr>
              <td>${window.SmartSpendApp.formatDate(item.transactionDate)}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.note)}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.walletName)}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.categoryName)}</td>
              <td>${statusBadge(item.type === "Income" ? "Safe" : "Warning")}</td>
              <td><strong>${window.SmartSpendApp.formatCurrency(item.amount)}</strong></td>
              <td>
                <div class="table-actions">
                  <button class="btn btn-secondary" data-edit-transaction="${item.transactionId}">Sửa</button>
                  <button class="btn btn-danger" data-delete-transaction="${item.transactionId}">Xóa</button>
                </div>
              </td>
            </tr>`
        )
        .join("");

      tableBody.querySelectorAll("[data-edit-transaction]").forEach((button) => {
        button.addEventListener("click", () => {
          const transaction = transactions.find((item) => item.transactionId === Number(button.dataset.editTransaction));
          if (!transaction || !transactionForm) {
            return;
          }

          editingTransactionId = transaction.transactionId;
          transactionForm.walletId.value = transaction.walletId;
          transactionForm.categoryId.value = transaction.categoryId;
          transactionForm.type.value = transaction.type;
          transactionForm.amount.value = transaction.amount;
          transactionForm.note.value = transaction.note;
          transactionForm.transactionDate.value = window.SmartSpendApp.formatDateInput(transaction.transactionDate);
          transactionForm.receiptImagePath.value = "";
          transactionForm.querySelector("button[type='submit']").textContent = "Cập nhật giao dịch";
          document.getElementById("cancelTransactionEdit").hidden = false;
          window.location.hash = "quick-add";
          window.scrollTo({ top: 0, behavior: "smooth" });
        });
      });

      tableBody.querySelectorAll("[data-delete-transaction]").forEach((button) => {
        button.addEventListener("click", async () => {
          if (!window.confirm("Bạn có chắc muốn xóa giao dịch này?")) {
            return;
          }

          try {
            await window.SmartSpendApp.api(`/api/transactions/${button.dataset.deleteTransaction}`, { method: "DELETE" });
            window.SmartSpendApp.showToast("Đã xóa giao dịch.", "success");
            await loadTransactions();
          } catch (error) {
            message("transactionMessage", error.message, "error");
          }
        });
      });
    }

    transactionForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("transactionMessage", "", "");
      try {
        await window.SmartSpendApp.api(editingTransactionId ? `/api/transactions/${editingTransactionId}` : "/api/transactions", {
          method: editingTransactionId ? "PUT" : "POST",
          body: {
            walletId: Number(transactionForm.walletId.value),
            categoryId: Number(transactionForm.categoryId.value),
            type: transactionForm.type.value,
            amount: Number(transactionForm.amount.value),
            note: transactionForm.note.value,
            transactionDate: `${transactionForm.transactionDate.value}T00:00:00Z`,
            receiptImagePath: transactionForm.receiptImagePath.value || null,
          },
        });

        message("transactionMessage", editingTransactionId ? "Cập nhật giao dịch thành công." : "Đã tạo giao dịch mới.", "success");
        resetTransactionForm();
        await loadTransactions();
      } catch (error) {
        message("transactionMessage", error.message, "error");
      }
    });

    filterForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      await loadTransactions();
    });

    document.getElementById("resetFilters")?.addEventListener("click", async () => {
      filterForm?.reset();
      await loadTransactions();
    });

    document.getElementById("exportTransactionsButton")?.addEventListener("click", async () => {
      message("transactionMessage", "", "");
      try {
        const token = window.AuthClient?.getAccessToken?.();
        const query = filterForm ? buildQueryFromFilters() : "";
        const headers = {};
        if (token) {
          headers.Authorization = `Bearer ${token}`;
        }

        const response = await fetch(`/api/transactions/export${query ? `?${query}` : ""}`, {
          method: "GET",
          headers,
          credentials: "same-origin",
        });

        if (!response.ok) {
          let errorMessage = "Xuất Excel thất bại.";
          const contentType = response.headers.get("content-type") || "";
          if (contentType.toLowerCase().includes("application/json")) {
            const errorBody = await response.json().catch(() => null);
            errorMessage = errorBody?.message || errorMessage;
          }
          throw new Error(errorMessage);
        }

        const blob = await response.blob();
        const disposition = response.headers.get("content-disposition") || "";
        const matchedName = /filename=\"?([^\";]+)\"?/i.exec(disposition);
        const fileName = matchedName?.[1] || `smartspend-transactions-${Date.now()}.xlsx`;

        const downloadUrl = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = downloadUrl;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(downloadUrl);
        window.SmartSpendApp.showToast("Da xuat file Excel.", "success");
      } catch (error) {
        message("transactionMessage", error.message, "error");
      }
    });

    document.getElementById("cancelTransactionEdit")?.addEventListener("click", resetTransactionForm);
    smartInputButton?.addEventListener("click", async () => {
      try {
        await applySmartInputSuggestion();
      } catch (error) {
        message("transactionMessage", error.message, "error");
      }
    });

    await loadLookups();
    resetTransactionForm();
    await loadTransactions();
  }

  async function initBudgetsPage() {
    const budgetForm = document.getElementById("budgetForm");
    const budgetList = document.getElementById("budgetList");
    let categories = [];

    async function loadCategories() {
      categories = await window.SmartSpendApp.api("/api/categories?type=Expense");
      const select = document.getElementById("budgetCategoryId");
      if (select) {
        select.innerHTML = ['<option value="">Chọn danh mục</option>']
          .concat(categories.map((item) => `<option value="${item.categoryId}">${window.SmartSpendApp.escapeHtml(item.name)}</option>`))
          .join("");
      }
    }

    async function loadBudgets() {
      const monthValue = document.getElementById("budgetMonth").value;
      const params = monthValue ? `?month=${monthValue}-01` : "";
      const budgets = await window.SmartSpendApp.api(`/api/budgets${params}`);
      const count = document.getElementById("budgetCount");
      if (count) {
        count.textContent = String(budgets.length);
      }

      if (!budgetList) {
        return;
      }

      if (budgets.length === 0) {
        budgetList.innerHTML = '<div class="empty-state">Chưa có ngân sách nào cho tháng đang chọn.</div>';
        return;
      }

      budgetList.innerHTML = budgets
        .map(
          (item) => `
            <div class="budget-row">
              <div class="panel-header" style="margin:0;">
                <div>
                  <strong>${window.SmartSpendApp.escapeHtml(item.categoryName)}</strong>
                  <div class="muted">Tháng ${window.SmartSpendApp.formatDate(item.month)}</div>
                </div>
                <div class="table-actions">
                  ${statusBadge(item.status)}
                  <button class="btn btn-danger" data-delete-budget="${item.budgetId}">Xóa</button>
                </div>
              </div>
              <div class="muted">${window.SmartSpendApp.formatCurrency(item.spentAmount)} / ${window.SmartSpendApp.formatCurrency(item.limitAmount)}</div>
              <div class="progress-track"><div class="progress-fill ${budgetTone(item.status)}" style="width:${Math.min(Number(item.progressPercentage || 0), 100)}%;"></div></div>
            </div>`
        )
        .join("");

      budgetList.querySelectorAll("[data-delete-budget]").forEach((button) => {
        button.addEventListener("click", async () => {
          if (!window.confirm("Xóa ngân sách này?")) {
            return;
          }

          try {
            await window.SmartSpendApp.api(`/api/budgets/${button.dataset.deleteBudget}`, { method: "DELETE" });
            window.SmartSpendApp.showToast("Đã xóa ngân sách.", "success");
            await loadBudgets();
          } catch (error) {
            message("budgetMessage", error.message, "error");
          }
        });
      });
    }

    budgetForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("budgetMessage", "", "");
      try {
        await window.SmartSpendApp.api("/api/budgets", {
          method: "POST",
          body: {
            categoryId: Number(budgetForm.categoryId.value),
            month: `${budgetForm.month.value}-01T00:00:00Z`,
            limitAmount: Number(budgetForm.limitAmount.value),
          },
        });

        message("budgetMessage", "Đã lưu ngân sách.", "success");
        await loadBudgets();
      } catch (error) {
        message("budgetMessage", error.message, "error");
      }
    });

    document.getElementById("reloadBudgets")?.addEventListener("click", async () => {
      await loadBudgets();
    });

    const monthField = document.getElementById("budgetMonth");
    if (monthField && !monthField.value) {
      monthField.value = new Date().toISOString().slice(0, 7);
    }

    await loadCategories();
    await loadBudgets();
  }

  async function initReportsPage() {
    const monthInput = document.getElementById("reportMonth");

    function renderSummary(prefix, data) {
      const mappings = {
        Income: data.totalIncome,
        Expense: data.totalExpense,
        Net: data.netAmount,
      };

      Object.entries(mappings).forEach(([suffix, value]) => {
        const node = document.getElementById(`${prefix}${suffix}`);
        if (node) {
          node.textContent = window.SmartSpendApp.formatCurrency(value);
        }
      });

      const countNode = document.getElementById(`${prefix}Count`);
      if (countNode) {
        countNode.textContent = String(data.transactionCount || 0);
      }

      const rangeNode = document.getElementById(`${prefix}Range`);
      if (rangeNode) {
        rangeNode.textContent = `${window.SmartSpendApp.formatDate(data.startDate)} - ${window.SmartSpendApp.formatDate(data.endDate)}`;
      }
    }

    function renderTopCategories(targetId, categories) {
      const container = document.getElementById(targetId);
      if (!container) {
        return;
      }

      if (!Array.isArray(categories) || categories.length === 0) {
        container.innerHTML = '<div class="empty-state">Chưa có chi tiêu để thống kê danh mục.</div>';
        return;
      }

      container.innerHTML = categories
        .map(
          (item) => `
            <div class="legend-item">
              <div class="inline-actions">
                <span class="legend-swatch" style="background:${window.SmartSpendApp.escapeHtml(item.color || "#48d1a0")}"></span>
                <span>${window.SmartSpendApp.escapeHtml(item.categoryName || "Khac")}</span>
              </div>
              <strong>${window.SmartSpendApp.formatCurrency(item.amount)} (${Number(item.percentage || 0).toFixed(1)}%)</strong>
            </div>`
        )
        .join("");
    }

    function renderEmailHistory(items) {
      const body = document.getElementById("reportEmailHistoryBody");
      if (!body) {
        return;
      }

      if (!Array.isArray(items) || items.length === 0) {
        body.innerHTML = '<tr><td colspan="5"><div class="empty-state">Chưa có lịch sử gửi email.</div></td></tr>';
        return;
      }

      body.innerHTML = items
        .map(
          (item) => `
            <tr>
              <td>${window.SmartSpendApp.escapeHtml(item.eventType || "--")}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.recipientEmail || "--")}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.subject || "--")}</td>
              <td>${window.SmartSpendApp.escapeHtml(item.metadata || "--")}</td>
              <td>${window.SmartSpendApp.formatDateTime(item.sentAt)}</td>
            </tr>`
        )
        .join("");
    }

    async function loadWeeklySummary() {
      const data = await window.SmartSpendApp.api("/api/reports/weekly");
      renderSummary("weekly", data);
      renderTopCategories("weeklyTopCategories", data.topExpenseCategories || []);
    }

    async function loadMonthlySummary() {
      const monthValue = monthInput?.value || "";
      const query = monthValue ? `?month=${encodeURIComponent(`${monthValue}-01`)}` : "";
      const data = await window.SmartSpendApp.api(`/api/reports/monthly${query}`);
      renderSummary("monthly", data);
      renderTopCategories("monthlyTopCategories", data.topExpenseCategories || []);
    }

    async function loadEmailHistory() {
      const data = await window.SmartSpendApp.api("/api/reports/email-history?take=25");
      renderEmailHistory(data || []);
    }

    const now = new Date();
    if (monthInput && !monthInput.value) {
      monthInput.value = now.toISOString().slice(0, 7);
    }

    document.getElementById("reloadWeeklyReport")?.addEventListener("click", async () => {
      message("reportsMessage", "", "");
      try {
        await loadWeeklySummary();
      } catch (error) {
        message("reportsMessage", error.message, "error");
      }
    });

    document.getElementById("reloadMonthlyReport")?.addEventListener("click", async () => {
      message("reportsMessage", "", "");
      try {
        await loadMonthlySummary();
      } catch (error) {
        message("reportsMessage", error.message, "error");
      }
    });

    document.getElementById("reloadEmailHistory")?.addEventListener("click", async () => {
      message("reportsMessage", "", "");
      try {
        await loadEmailHistory();
      } catch (error) {
        message("reportsMessage", error.message, "error");
      }
    });

    try {
      await Promise.all([loadWeeklySummary(), loadMonthlySummary(), loadEmailHistory()]);
    } catch (error) {
      message("reportsMessage", error.message, "error");
    }
  }

  async function initProfilePage() {
    const profileForm = document.getElementById("profileForm");
    const passwordForm = document.getElementById("passwordForm");
    const avatarDot = document.getElementById("profileAvatarDot");
    const avatarImage = document.getElementById("profileAvatarImage");

    function getInitials(fullName, username) {
      const source = String(fullName || username || "Người dùng").trim();
      const parts = source.split(/\s+/).filter(Boolean);
      if (parts.length === 0) {
        return "U";
      }

      if (parts.length === 1) {
        return parts[0].slice(0, 2).toUpperCase();
      }

      return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
    }

    function renderProfileAvatar(profile) {
      if (!avatarDot || !avatarImage) {
        return;
      }

      const initials = getInitials(profile.fullName, profile.username);
      avatarDot.textContent = initials;

      const avatarUrl = String(profile.avatarUrl || "").trim();
      if (!avatarUrl) {
        avatarImage.hidden = true;
        avatarDot.hidden = false;
        avatarImage.removeAttribute("src");
        return;
      }

      avatarImage.onload = () => {
        avatarImage.hidden = false;
        avatarDot.hidden = true;
      };

      avatarImage.onerror = () => {
        avatarImage.hidden = true;
        avatarDot.hidden = false;
      };

      avatarImage.src = avatarUrl;
    }

    async function loadProfile() {
      const profile = await window.SmartSpendApp.api("/api/profile");
      document.getElementById("profileCreatedAt").textContent = window.SmartSpendApp.formatDate(profile.createdAt);
      document.getElementById("profileEmailVerified").textContent = profile.isEmailVerified ? "Đã xác thực OTP" : "Chưa xác thực";
      document.getElementById("profileLockStatus").textContent = profile.isLocked ? "Đang bị khóa" : "Hoạt động bình thường";
      profileForm.fullName.value = profile.fullName || "";
      profileForm.email.value = profile.email || "";
      profileForm.username.value = profile.username || "";
      profileForm.avatarUrl.value = profile.avatarUrl || "";
      renderProfileAvatar(profile);
    }

    profileForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("profileMessage", "", "");
      try {
        await window.SmartSpendApp.api("/api/profile", {
          method: "PUT",
          body: {
            fullName: profileForm.fullName.value,
            email: profileForm.email.value,
            avatarUrl: profileForm.avatarUrl.value || null,
          },
        });
        await loadProfile();
        message("profileMessage", "Đã cập nhật hồ sơ.", "success");
      } catch (error) {
        message("profileMessage", error.message, "error");
      }
    });

    passwordForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("passwordMessage", "", "");
      try {
        await window.SmartSpendApp.api("/api/profile/change-password", {
          method: "POST",
          body: {
            currentPassword: passwordForm.currentPassword.value,
            newPassword: passwordForm.newPassword.value,
            confirmPassword: passwordForm.confirmPassword.value,
          },
        });
        passwordForm.reset();
        message("passwordMessage", "Đổi mật khẩu thành công.", "success");
      } catch (error) {
        message("passwordMessage", error.message, "error");
      }
    });

    await loadProfile();
  }

  async function initAdminDashboardPage() {
    try {
      const summary = await window.SmartSpendApp.api("/api/admin/summary");
      document.getElementById("adminNewUsersToday").textContent = String(summary.newUsersToday || 0);
      document.getElementById("adminTransactionsToday").textContent = String(summary.transactionsToday || 0);
      document.getElementById("adminTotalUsers").textContent = String(summary.totalUsers || 0);
      document.getElementById("adminTotalKeywords").textContent = String(summary.totalKeywords || 0);
    } catch (error) {
      message("adminDashboardMessage", error.message, "error");
    }
  }

  async function initAdminUsersPage() {
    const body = document.getElementById("adminUsersBody");

    async function loadUsers() {
      const users = await window.SmartSpendApp.api("/api/admin/users");
      if (!body) {
        return;
      }

      if (users.length === 0) {
        body.innerHTML = '<tr><td colspan="7"><div class="empty-state">Chưa có tài khoản nào.</div></td></tr>';
        return;
      }

      body.innerHTML = users
        .map(
          (user) => `
            <tr>
              <td>${user.userId}</td>
              <td>${window.SmartSpendApp.escapeHtml(user.fullName)}</td>
              <td>${window.SmartSpendApp.escapeHtml(user.username)}</td>
              <td>${window.SmartSpendApp.escapeHtml(user.email)}</td>
              <td>${window.SmartSpendApp.escapeHtml(normalizeRoleLabel(user.role))}</td>
              <td>${user.isLocked ? '<span class="status-pill status-danger">Đã khóa</span>' : '<span class="status-pill status-safe">Đang hoạt động</span>'}</td>
              <td>
                <button class="btn ${user.isLocked ? "btn-success" : "btn-warning"}" data-user-action="${user.isLocked ? "unlock" : "lock"}" data-user-id="${user.userId}">
                  ${user.isLocked ? "Mở khóa" : "Khóa"}
                </button>
              </td>
            </tr>`
        )
        .join("");

      body.querySelectorAll("[data-user-action]").forEach((button) => {
        button.addEventListener("click", async () => {
          try {
            await window.SmartSpendApp.api(`/api/admin/users/${button.dataset.userId}/${button.dataset.userAction}`, {
              method: "POST",
            });
            window.SmartSpendApp.showToast("Cập nhật trạng thái tài khoản thành công.", "success");
            await loadUsers();
          } catch (error) {
            message("adminUsersMessage", error.message, "error");
          }
        });
      });
    }

    await loadUsers();
  }

  async function initAdminCategoriesPage() {
    const form = document.getElementById("adminCategoryForm");
    const body = document.getElementById("adminCategoriesBody");
    const cancelButton = document.getElementById("cancelAdminCategoryEdit");
    const nameInput = document.getElementById("adminCategoryName");
    const typeSelect = document.getElementById("adminCategoryType");
    const iconInput = document.getElementById("adminCategoryIcon");
    const colorInput = document.getElementById("adminCategoryColor");
    const isSystemInput = document.getElementById("adminCategoryIsSystem");
    let editingCategoryId = null;
    let categories = [];

    function resetForm() {
      editingCategoryId = null;
      form?.reset();
      if (typeSelect) {
        typeSelect.value = "Expense";
      }
      if (iconInput) {
        iconInput.value = "circle";
      }
      if (colorInput) {
        colorInput.value = "#48d1a0";
      }
      if (isSystemInput) {
        isSystemInput.checked = false;
      }
      const submitButton = form?.querySelector("button[type='submit']");
      if (submitButton) {
        submitButton.textContent = "Lưu danh mục";
      }
      if (cancelButton) {
        cancelButton.hidden = true;
      }
    }

    function renderTable() {
      if (!body) {
        return;
      }

      if (!Array.isArray(categories) || categories.length === 0) {
        body.innerHTML = '<tr><td colspan="7"><div class="empty-state">Chưa có danh mục nào.</div></td></tr>';
        return;
      }

      body.innerHTML = categories
        .map(
          (category) => `
            <tr>
              <td>${category.categoryId}</td>
              <td>${window.SmartSpendApp.escapeHtml(category.name)}</td>
              <td>${window.SmartSpendApp.escapeHtml(category.type === "Income" ? "Thu nhập" : "Chi tiêu")}</td>
              <td>${window.SmartSpendApp.escapeHtml(category.icon || "--")}</td>
              <td><span class="legend-swatch" style="background:${window.SmartSpendApp.escapeHtml(category.color || "#48d1a0")}"></span> ${window.SmartSpendApp.escapeHtml(category.color || "--")}</td>
              <td>${category.isSystem ? '<span class="status-pill status-safe">Hệ thống</span>' : '<span class="status-pill">Tùy chỉnh</span>'}</td>
              <td>
                <div class="table-actions">
                  <button class="btn btn-secondary" type="button" data-edit-category="${category.categoryId}">Sửa</button>
                  <button class="btn btn-danger" type="button" data-delete-category="${category.categoryId}">Xóa</button>
                </div>
              </td>
            </tr>`
        )
        .join("");

      body.querySelectorAll("[data-edit-category]").forEach((button) => {
        button.addEventListener("click", () => {
          const categoryId = Number(button.dataset.editCategory);
          const category = categories.find((item) => Number(item.categoryId) === categoryId);
          if (!category || !form) {
            return;
          }

          editingCategoryId = categoryId;
          if (nameInput) {
            nameInput.value = category.name || "";
          }
          if (typeSelect) {
            typeSelect.value = category.type || "Expense";
          }
          if (iconInput) {
            iconInput.value = category.icon || "circle";
          }
          if (colorInput) {
            colorInput.value = category.color || "#48d1a0";
          }
          if (isSystemInput) {
            isSystemInput.checked = Boolean(category.isSystem);
          }

          const submitButton = form.querySelector("button[type='submit']");
          if (submitButton) {
            submitButton.textContent = "Cập nhật danh mục";
          }
          if (cancelButton) {
            cancelButton.hidden = false;
          }
          window.scrollTo({ top: 0, behavior: "smooth" });
        });
      });

      body.querySelectorAll("[data-delete-category]").forEach((button) => {
        button.addEventListener("click", async () => {
          if (!window.confirm("Bạn có chắc muốn xóa danh mục này?")) {
            return;
          }

          try {
            await window.SmartSpendApp.api(`/api/admin/categories/${button.dataset.deleteCategory}`, {
              method: "DELETE",
            });
            window.SmartSpendApp.showToast("Đã xóa danh mục.", "success");
            await loadCategories();
            if (editingCategoryId === Number(button.dataset.deleteCategory)) {
              resetForm();
            }
          } catch (error) {
            message("adminCategoriesMessage", error.message, "error");
          }
        });
      });
    }

    async function loadCategories() {
      categories = await window.SmartSpendApp.api("/api/admin/categories");
      renderTable();
    }

    form?.addEventListener("submit", async (event) => {
      event.preventDefault();
      message("adminCategoriesMessage", "", "");
      try {
        const payload = {
          name: nameInput?.value || "",
          type: typeSelect?.value || "",
          icon: iconInput?.value || "",
          color: colorInput?.value || "",
          isSystem: Boolean(isSystemInput?.checked),
        };

        await window.SmartSpendApp.api(
          editingCategoryId ? `/api/admin/categories/${editingCategoryId}` : "/api/admin/categories",
          {
            method: editingCategoryId ? "PUT" : "POST",
            body: payload,
          }
        );

        window.SmartSpendApp.showToast(
          editingCategoryId ? "Đã cập nhật danh mục." : "Đã thêm danh mục mới.",
          "success"
        );
        resetForm();
        await loadCategories();
      } catch (error) {
        message("adminCategoriesMessage", error.message, "error");
      }
    });

    cancelButton?.addEventListener("click", resetForm);
    document.getElementById("reloadAdminCategories")?.addEventListener("click", async () => {
      message("adminCategoriesMessage", "", "");
      try {
        await loadCategories();
      } catch (error) {
        message("adminCategoriesMessage", error.message, "error");
      }
    });

    resetForm();
    await loadCategories();
  }

  async function initAdminLogsPage() {
    const body = document.getElementById("adminLogsBody");
    const takeInput = document.getElementById("adminLogsTake");

    function localizeAuditAction(action) {
      const map = {
        WalletCreated: "Tạo ví",
        WalletUpdated: "Cập nhật ví",
        WalletDeleted: "Xóa ví",
        WalletTransferred: "Chuyển tiền giữa ví",
        TransactionCreated: "Tạo giao dịch",
        TransactionUpdated: "Cập nhật giao dịch",
        TransactionDeleted: "Xóa giao dịch",
        UserLoginSuccess: "Đăng nhập thành công",
        UserLoginNewDeviceAlertSent: "Gửi cảnh báo thiết bị mới",
        WeeklySummarySent: "Gửi tổng kết tuần",
      };

      return map[action] || action || "--";
    }

    function localizeTargetType(targetType) {
      const map = {
        User: "Người dùng",
        Wallet: "Ví",
        Transaction: "Giao dịch",
        Transfer: "Chuyển khoản",
        Email: "Email",
      };

      return map[targetType] || targetType || "--";
    }

    function renderLogs(logs) {
      if (!body) {
        return;
      }

      if (!Array.isArray(logs) || logs.length === 0) {
        body.innerHTML = '<tr><td colspan="7"><div class="empty-state">Chưa có bản ghi nhật ký nào.</div></td></tr>';
        return;
      }

      body.innerHTML = logs
        .map(
          (log) => `
            <tr>
              <td>${log.auditLogId}</td>
              <td>${window.SmartSpendApp.escapeHtml(log.actor || "Hệ thống")}</td>
              <td>${window.SmartSpendApp.escapeHtml(localizeAuditAction(log.action))}</td>
              <td>${window.SmartSpendApp.escapeHtml(localizeTargetType(log.targetType))}</td>
              <td>${window.SmartSpendApp.escapeHtml(log.targetId || "--")}</td>
              <td>${window.SmartSpendApp.escapeHtml(log.metadata || "--")}</td>
              <td>${window.SmartSpendApp.formatDateTime(log.createdAt)}</td>
            </tr>`
        )
        .join("");
    }

    async function loadLogs() {
      const requestedTake = Number(takeInput?.value || 50);
      const take = Number.isFinite(requestedTake) ? Math.min(Math.max(requestedTake, 1), 200) : 50;
      if (takeInput) {
        takeInput.value = String(take);
      }
      const logs = await window.SmartSpendApp.api(`/api/admin/audit-logs?take=${take}`);
      renderLogs(logs);
    }

    document.getElementById("reloadAdminLogs")?.addEventListener("click", async () => {
      message("adminLogsMessage", "", "");
      try {
        await loadLogs();
      } catch (error) {
        message("adminLogsMessage", error.message, "error");
      }
    });

    takeInput?.addEventListener("change", async () => {
      message("adminLogsMessage", "", "");
      try {
        await loadLogs();
      } catch (error) {
        message("adminLogsMessage", error.message, "error");
      }
    });

    try {
      await loadLogs();
    } catch (error) {
      message("adminLogsMessage", error.message, "error");
    }
  }

  document.addEventListener("smartspend:ready", () => {
    const page = document.body.dataset.page || "";
    const handler = appReadyHandlers[page];
    if (handler) {
      handler().catch((error) => {
        window.SmartSpendApp.showToast(error.message, "error");
      });
    }
  });
})();
