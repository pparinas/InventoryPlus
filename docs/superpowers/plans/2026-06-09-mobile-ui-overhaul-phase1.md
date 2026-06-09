# Mobile UI Overhaul — Phase 1: Foundation, App Shell & Dashboard

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the mobile-native design system (CSS + shared Blazor components), replace the mobile navigation with a tab bar + FAB shell, redesign the Dashboard as the proving ground, and remove dead offline-queue code.

**Architecture:** Blazor WebAssembly (.NET 10) SPA with a single custom CSS file (`wwwroot/css/app.css`) driven by CSS variables for theming (dark default, light mode, 7 color schemes). New reusable components (`BottomSheet`, `ListRow`, `StatCard`, `SegmentedControl`) go in `Components/`. The app shell lives in `Layout/MainLayout.razor(.cs)`. No test project exists — verification is `dotnet build` + visual checks per the approved spec (`docs/superpowers/specs/2026-06-09-mobile-ui-overhaul-design.md`).

**Tech Stack:** Blazor WASM, custom CSS (no framework), Font Awesome 6 icons, Space Grotesk/Space Mono fonts, Supabase backend (untouched in this plan).

**Spec deviation note:** The spec called for "persist the offline pending-write queue." Investigation showed the queue is dead code — nothing ever enqueues to `_pendingWrites`, `SavePendingQueueAsync` is never called, and offline mutations are rejected by design (callers get the exception; the offline banner says changes require a connection). Task 6 therefore REMOVES the vestigial queue instead of extending it. Implementing real offline writes would be a new feature, out of scope per the spec's "UI-driven cleanup" decision.

**Out of scope for Phase 1** (later plans): POS/Sales redesign, Products/Stocks/Expenses/Admin list pages, Reports/Settings, auth/landing polish.

**Working directory:** `D:\pp\InventoryPlus`. All commands run from there. Build with `dotnet build` (expect `Build succeeded`). The app targets mobile-first: after UI tasks, verify visually with `dotnet run` at ~390px width (browser devtools device mode) in dark AND light themes.

---

### Task 1: Design-system CSS additions

**Files:**
- Modify: `wwwroot/css/app.css` (append at end of file, after the `#blazor-error-ui .dismiss` rule on line ~691)

- [ ] **Step 1: Append the new design-system CSS block to `wwwroot/css/app.css`**

Append exactly this block at the end of the file:

```css

/* ═══════════════════════════════════════════
   MOBILE-NATIVE OVERHAUL — Phase 1 additions
   ═══════════════════════════════════════════ */

/* ── TAB BAR (mobile bottom navigation) ── */
.tab-bar{display:none;position:fixed;bottom:0;left:0;right:0;background:var(--bg2);border-top:1px solid var(--border);z-index:150;align-items:stretch;justify-content:space-around;padding-bottom:env(safe-area-inset-bottom,0px);padding-left:env(safe-area-inset-left,0px);padding-right:env(safe-area-inset-right,0px)}
@media(max-width:767px){.tab-bar{display:flex}}
.tab-item{flex:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;min-height:56px;color:var(--fg3);font-size:10px;font-weight:600;cursor:pointer;border:none;background:none;letter-spacing:.02em;transition:color .12s;text-decoration:none;font-family:inherit;padding:6px 0}
.tab-item i{font-size:17px}
.tab-item.active{color:var(--lime)}
.tab-fab-slot{flex:1;display:flex;align-items:flex-start;justify-content:center;position:relative}
.tab-fab{width:48px;height:48px;border-radius:50%;background:var(--lime);color:#0a0a0a;border:none;font-size:18px;display:flex;align-items:center;justify-content:center;margin-top:-18px;box-shadow:0 6px 18px var(--lime-mid);cursor:pointer;transition:transform .15s}
.tab-fab:active{transform:scale(.92)}
[data-theme="light"] .tab-fab{color:#fff}

/* ── BOTTOM SHEET ── */
.sheet-overlay{position:fixed;inset:0;background:rgba(0,0,0,.6);z-index:400;animation:fadeIn .15s ease}
.sheet{position:fixed;left:0;right:0;bottom:0;z-index:401;background:var(--bg2);border:1px solid var(--border2);border-bottom:none;border-radius:16px 16px 0 0;max-height:85vh;display:flex;flex-direction:column;transform:translateY(105%);transition:transform .25s cubic-bezier(.4,0,.2,1),visibility .25s;padding-bottom:env(safe-area-inset-bottom,0px);visibility:hidden}
.sheet.open{transform:translateY(0);visibility:visible}
.sheet-handle{width:36px;height:4px;background:var(--bg5);border-radius:99px;margin:10px auto 4px;flex-shrink:0;cursor:pointer}
.sheet-header{display:flex;align-items:center;justify-content:space-between;padding:6px 18px 10px;border-bottom:1px solid var(--border);flex-shrink:0}
.sheet-header h3{font-size:14px;font-weight:700;color:var(--fg);margin:0}
.sheet-close{width:28px;height:28px;border-radius:var(--radius-sm);border:1px solid var(--border);background:none;color:var(--fg2);display:flex;align-items:center;justify-content:center;cursor:pointer;font-size:12px;padding:0}
.sheet-body{padding:14px 18px 18px;overflow-y:auto}
@media(min-width:768px){.sheet{left:50%;right:auto;width:480px;transform:translate(-50%,105%)}.sheet.open{transform:translate(-50%,0)}}

/* ── LIST ROWS ── */
.list-stack{display:flex;flex-direction:column;gap:8px}
.list-row{display:flex;align-items:center;gap:11px;padding:11px 13px;background:var(--bg2);border:1px solid var(--border);border-radius:var(--radius-lg);min-height:52px}
.list-row.clickable{cursor:pointer;transition:background .12s,border-color .12s}
.list-row.clickable:hover{border-color:var(--border2);background:var(--bg3)}
.list-row.clickable:active{background:var(--bg3)}
.list-row.flat{background:none;border:none;border-radius:0;border-bottom:1px solid var(--border);min-height:48px;padding:10px 2px}
.list-row.flat:last-child{border-bottom:none}
.list-row-icon{width:34px;height:34px;border-radius:9px;display:flex;align-items:center;justify-content:center;font-size:14px;flex-shrink:0;background:var(--bg3);color:var(--fg2)}
.list-row-body{flex:1;min-width:0}
.list-row-title{font-size:13px;font-weight:600;color:var(--fg);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.list-row-sub{font-size:11px;color:var(--fg3);margin-top:1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.list-row-trailing{flex-shrink:0;text-align:right;font-size:12px;font-weight:600;color:var(--fg)}

/* ── SEGMENTED CONTROL ── */
.seg-control{display:inline-flex;background:var(--bg3);border:1px solid var(--border);border-radius:9px;padding:3px;gap:2px;max-width:100%;overflow-x:auto}
.seg-item{padding:6px 12px;min-height:32px;border-radius:7px;border:none;background:none;color:var(--fg2);font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap;transition:all .12s;font-family:inherit}
.seg-item.active{background:var(--bg2);color:var(--fg);box-shadow:0 1px 3px rgba(0,0,0,.25)}
.seg-item:disabled{opacity:.45;cursor:not-allowed}
[data-theme="light"] .seg-item.active{box-shadow:0 1px 3px rgba(0,0,0,.12)}

/* ── HERO HEADER (Dashboard) ── */
.hero-head{display:flex;flex-direction:column;gap:2px;min-width:0}
.hero-greet{font-size:12px;color:var(--fg2)}
.hero-value{font-size:30px;font-weight:700;letter-spacing:-1px;color:var(--fg);font-family:'Space Mono',monospace;line-height:1.15;display:flex;align-items:baseline;gap:8px;flex-wrap:wrap}
.hero-label{font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:var(--fg3)}
.trend-badge{font-size:11px;font-weight:700;padding:2px 8px;border-radius:99px;font-family:'Space Grotesk',sans-serif;display:inline-flex;align-items:center;gap:4px}
.trend-badge.up{background:var(--green-dim);color:var(--green)}
.trend-badge.down{background:var(--red-dim);color:var(--red)}

/* ── QUICK ACTION BUTTONS ── */
.action-row{display:flex;gap:8px;flex-wrap:wrap}
.action-btn{flex:1;display:inline-flex;align-items:center;justify-content:center;gap:7px;min-height:46px;border-radius:var(--radius-lg);font-size:13px;font-weight:700;border:none;cursor:pointer;transition:all .12s;font-family:inherit;white-space:nowrap}
@media(min-width:768px){.action-btn{flex:0 0 auto;padding:0 22px}}
.action-btn.primary{background:var(--lime);color:#0a0a0a}
[data-theme="light"] .action-btn.primary{color:#fff}
.action-btn.ghost{background:var(--bg2);color:var(--fg);border:1px solid var(--border)}
.action-btn:active{transform:scale(.98)}

/* ── STAT SCROLLER (horizontal on mobile, grid on desktop) ── */
.stat-scroll{display:grid;grid-template-columns:repeat(2,1fr);gap:10px}
@media(min-width:1024px){.stat-scroll{grid-template-columns:repeat(4,1fr)}}
@media(max-width:767px){.stat-scroll{display:flex;gap:9px;overflow-x:auto;scroll-snap-type:x mandatory;margin:0 -14px;padding:0 14px 4px;-webkit-overflow-scrolling:touch}.stat-scroll::-webkit-scrollbar{display:none}.stat-scroll .stat-card{min-width:130px;flex-shrink:0;scroll-snap-align:start}}

/* ── SHEET MENU ── */
.sheet-menu{display:flex;flex-direction:column;gap:6px}

/* ── TOPBAR LOGO (replaces inline style) ── */
.topbar-logo{height:28px;width:auto;max-width:100px;object-fit:contain;border-radius:4px}

/* ── TOUCH TARGETS (mobile) ── */
@media(max-width:767px){
  .btn{min-height:40px}
  .btn-sm{min-height:36px}
  .form-control,.form-select{min-height:42px;font-size:16px}
  .topbar-btn{width:38px;height:38px}
  .qty-btn,.pos-qty-btn{width:30px;height:30px}
  .pill,.pill-btn{padding:7px 13px}
}

/* ── DESKTOP CONTENT WIDTH ── */
.page-inner{width:100%;max-width:1140px;margin:0 auto}
```

Note: mobile `form-control` font-size is deliberately 16px — iOS Safari zooms the page on focus for anything smaller.

- [ ] **Step 2: Build to confirm nothing broke**

Run: `dotnet build`
Expected: `Build succeeded` (CSS is not compiled, this is a sanity check only).

- [ ] **Step 3: Commit**

```powershell
git add wwwroot/css/app.css
git commit -m "feat: add mobile-native design system CSS (tab bar, sheet, list rows, seg control)"
```

---

### Task 2: BottomSheet component

**Files:**
- Create: `Components/BottomSheet.razor`

- [ ] **Step 1: Create `Components/BottomSheet.razor`**

```razor
@* Slide-up bottom sheet. Always rendered (hidden via CSS) so the slide animation works.
   Usage: <BottomSheet @bind-IsOpen="showSheet" Title="Quick Actions">...</BottomSheet> *@

@if (IsOpen)
{
    <div class="sheet-overlay" @onclick="Close"></div>
}
<div class="sheet @(IsOpen ? "open" : "")" @onclick:stopPropagation>
    <div class="sheet-handle" @onclick="Close"></div>
    @if (!string.IsNullOrEmpty(Title))
    {
        <div class="sheet-header">
            <h3>@Title</h3>
            <button type="button" class="sheet-close" @onclick="Close" aria-label="Close">
                <i class="fa-solid fa-xmark"></i>
            </button>
        </div>
    }
    <div class="sheet-body">
        @ChildContent
    </div>
</div>

@code {
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private Task Close() => IsOpenChanged.InvokeAsync(false);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 3: Commit**

```powershell
git add Components/BottomSheet.razor
git commit -m "feat: add BottomSheet component"
```

---

### Task 3: ListRow, StatCard, SegmentedControl components

**Files:**
- Create: `Components/ListRow.razor`
- Create: `Components/StatCard.razor`
- Create: `Components/SegmentedControl.razor`

- [ ] **Step 1: Create `Components/ListRow.razor`**

```razor
@* Icon + title/sub + trailing content row. Card style by default; Flat=true for rows inside a card.
   IconClass accepts the existing si-* classes (si-lime, si-green, si-amber, si-red, si-cyan). *@

<div class="list-row @(Flat ? "flat" : "") @(OnClick.HasDelegate ? "clickable" : "")" @onclick="HandleClick">
    @if (!string.IsNullOrEmpty(Icon))
    {
        <div class="list-row-icon @IconClass"><i class="@Icon"></i></div>
    }
    <div class="list-row-body">
        <div class="list-row-title">@Title</div>
        @if (!string.IsNullOrEmpty(Sub))
        {
            <div class="list-row-sub">@Sub</div>
        }
    </div>
    @if (Trailing != null)
    {
        <div class="list-row-trailing">@Trailing</div>
    }
</div>

@code {
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string? IconClass { get; set; }
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string? Sub { get; set; }
    [Parameter] public bool Flat { get; set; }
    [Parameter] public RenderFragment? Trailing { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private Task HandleClick() => OnClick.HasDelegate ? OnClick.InvokeAsync() : Task.CompletedTask;
}
```

- [ ] **Step 2: Create `Components/StatCard.razor`**

```razor
@* KPI card reusing the existing .stat-card CSS. *@

<div class="stat-card">
    <div class="stat-card-icon @IconClass"><i class="@Icon"></i></div>
    <div class="stat-card-value">@Value</div>
    <div class="stat-card-label">@Label</div>
    @if (!string.IsNullOrEmpty(Sub))
    {
        <div class="stat-card-sub">@Sub</div>
    }
</div>

@code {
    [Parameter] public string Icon { get; set; } = "fa-solid fa-chart-line";
    [Parameter] public string IconClass { get; set; } = "si-lime";
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string? Sub { get; set; }
}
```

- [ ] **Step 3: Create `Components/SegmentedControl.razor`**

```razor
@* Thumb-friendly segmented switcher.
   Usage: <SegmentedControl Options="opts" @bind-Value="selected" />
   where opts is List<SegmentedControl.SegOption>. *@

<div class="seg-control" role="tablist">
    @foreach (var opt in Options)
    {
        var value = opt.Value;
        <button type="button" class="seg-item @(value == Value ? "active" : "")"
                disabled="@opt.Disabled" role="tab"
                @onclick="() => SelectAsync(value)">@opt.Label</button>
    }
</div>

@code {
    public record SegOption(string Value, string Label, bool Disabled = false);

    [Parameter] public List<SegOption> Options { get; set; } = new();
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    private Task SelectAsync(string value) => ValueChanged.InvokeAsync(value);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```powershell
git add Components/ListRow.razor Components/StatCard.razor Components/SegmentedControl.razor
git commit -m "feat: add ListRow, StatCard, SegmentedControl components"
```

---

### Task 4: App shell — tab bar with FAB, quick-action & More sheets

**Files:**
- Modify: `Layout/MainLayout.razor` (full file replacement below)
- Modify: `Layout/MainLayout.razor.cs` (targeted edits below)
- Modify: `wwwroot/css/app.css` (remove obsolete mobile bottom bar CSS)

Removes the mobile hamburger/off-canvas sidebar (sidebar becomes desktop-only — existing CSS already hides it under 768px), replaces the old `mobile-bottom-bar` with the new `tab-bar` + FAB, and adds two bottom sheets.

- [ ] **Step 1: Replace the entire content of `Layout/MainLayout.razor` with:**

```razor
@inherits LayoutComponentBase

<div class="app-container" @onclick="HandleGlobalClick" @onkeydown="HandleKeyDown" tabindex="0">


    @if (Inventory.IsOffline)
    {
        <div class="offline-banner">
            <i class="fa-solid fa-wifi" style="opacity:0.6;"></i>
            Offline &mdash; showing cached data. Changes require an internet connection.
        </div>
    }

    <aside class="sidebar">
        <div class="sidebar-logo">
            <div class="sidebar-logo-mark"><span>N</span></div>
            <span class="sidebar-logo-text">Negosyo<b>360</b></span>
        </div>
        <NavMenu />
    </aside>

    <div class="main">
    <header class="topbar">
        <span class="topbar-title">
            @if(!string.IsNullOrEmpty(AppSettings.CustomLogoUrl))
            {
                <img src="@AppSettings.CustomLogoUrl" class="topbar-logo" alt="@AppSettings.CompanyName" />
            }
            @if(!AppSettings.UseLogoForBranding || string.IsNullOrEmpty(AppSettings.CustomLogoUrl))
            {
                @AppSettings.CompanyName
            }
        </span>
        <div class="topbar-spacer"></div>

        <button class="topbar-btn" @onclick="ToggleTheme" @onclick:stopPropagation title="Toggle Dark/Light Mode">
            <i class="fa-solid @(isLightMode ? "fa-moon" : "fa-sun")"></i>
        </button>

        @if (!AppSettings.IsPosMode)
        {
        <button class="topbar-btn" @onclick="ToggleNotifications" @onclick:stopPropagation title="Notifications">
            <i class="fa-solid fa-bell"></i>
            @if ((notificationPanel?.UnreadCount ?? 0) > 0)
            {
                <span class="notification-badge">@(notificationPanel!.UnreadCount > 9 ? "9+" : notificationPanel.UnreadCount.ToString())</span>
            }
        </button>

        <div class="dropdown" @onclick:stopPropagation>
            <button class="topbar-btn" @onclick="ToggleDropdown" title="Account">
                <i class="fa-solid fa-circle-user"></i>
            </button>

            @if (showDropdown)
            {
                <div class="dropdown-menu show">
                    <div class="d-flex align-items-center gap-3 mb-2" style="padding:8px 12px;">
                        <div class="user-avatar">
                            <i class="fa-solid fa-user" style="font-size:10px;"></i>
                        </div>
                        <div style="min-width: 0;">
                            <div class="user-name">@(currentUserEmail ?? "Loading...")</div>
                            <div class="user-role text-truncate">@AppSettings.CompanyName</div>
                        </div>
                    </div>

                    <hr>

                    <AuthorizeView>
                        <Authorized>
                            <a class="dropdown-item" href="settings" @onclick="() => showDropdown = false">
                                <i class="fa-solid fa-gear fa-fw"></i> Settings
                            </a>
                        </Authorized>
                    </AuthorizeView>

                    <button class="dropdown-item" style="color:var(--red);" @onclick="SignOut">
                        <i class="fa-solid fa-arrow-right-from-bracket"></i> Sign Out
                    </button>
                </div>
            }
        </div>
        }
    </header>

    <main class="page-content">
        <div class="page-inner">
            <ErrorBoundary @ref="errorBoundary">
                <ChildContent>
                    @Body
                </ChildContent>
                <ErrorContent Context="ex">
                    <div class="card card-p p-4" style="max-width: 500px; margin: 2rem auto;">
                        <i class="fa-solid fa-triangle-exclamation fa-3x mb-3" style="color: var(--warning);"></i>
                        <h3>Something went wrong</h3>
                        <p class="text-muted">An unexpected error occurred. Please try again.</p>
                        <button class="btn btn-primary" @onclick="ResetError"><i class="fa-solid fa-rotate-right me-1"></i> Try Again</button>
                    </div>
                </ErrorContent>
            </ErrorBoundary>
        </div>
    </main>
    </div> @* end .main *@

    @* ── Mobile Tab Bar ── *@
    <nav class="tab-bar">
        @if (AppSettings.IsPosMode)
        {
            <button class="tab-item @(AppSettings.PosActiveView == "pos" ? "active" : "")" @onclick="SetPosView">
                <i class="fa-solid fa-cash-register"></i>
                <span>POS</span>
            </button>
            <button class="tab-item @(AppSettings.PosActiveView == "history" ? "active" : "")" @onclick="SetPosHistoryView">
                <i class="fa-solid fa-clock-rotate-left"></i>
                <span>History</span>
            </button>
        }
        else
        {
            <a href="dashboard" class="tab-item @(IsActive("dashboard") ? "active" : "")">
                <i class="fa-solid fa-house"></i>
                <span>Home</span>
            </a>
            <a href="sales" class="tab-item @(IsActive("sales") ? "active" : "")">
                <i class="fa-solid fa-cash-register"></i>
                <span>Sales</span>
            </a>
            <div class="tab-fab-slot">
                <button class="tab-fab" @onclick="OpenQuickActions" @onclick:stopPropagation aria-label="Quick actions">
                    <i class="fa-solid fa-plus"></i>
                </button>
            </div>
            <a href="products" class="tab-item @(IsActive("products") ? "active" : "")">
                <i class="fa-solid fa-leaf"></i>
                <span>Products</span>
            </a>
            <button class="tab-item @(showMoreSheet ? "active" : "")" @onclick="OpenMoreSheet" @onclick:stopPropagation>
                <i class="fa-solid fa-ellipsis"></i>
                <span>More</span>
            </button>
        }
    </nav>

    @* ── Quick Actions Sheet (FAB) ── *@
    <BottomSheet @bind-IsOpen="showQuickSheet" Title="Quick Actions">
        <div class="sheet-menu">
            <ListRow Icon="fa-solid fa-cart-shopping" IconClass="si-lime" Title="New Sale" Sub="Ring up a sale" OnClick='() => GoTo("sales")' />
            @if (AppSettings.ShowInventoryTab)
            {
                <ListRow Icon="fa-solid fa-boxes-stacked" IconClass="si-amber" Title="Add Stock" Sub="Restock ingredients & materials" OnClick='() => GoTo("stocks")' />
            }
            <ListRow Icon="fa-solid fa-leaf" IconClass="si-green" Title="New Product" Sub="Create a product to sell" OnClick='() => GoTo("products")' />
            @if (AppSettings.ShowOpexTab)
            {
                <ListRow Icon="fa-solid fa-file-invoice-dollar" IconClass="si-red" Title="New Expense" Sub="Record an operating expense" OnClick='() => GoTo("expenses")' />
            }
        </div>
    </BottomSheet>

    @* ── More Sheet ── *@
    <BottomSheet @bind-IsOpen="showMoreSheet" Title="More">
        <div class="sheet-menu">
            @if (AppSettings.ShowInventoryTab)
            {
                <ListRow Icon="fa-solid fa-boxes-stacked" IconClass="si-amber" Title="Inventory" Sub="Stock levels & raw materials" OnClick='() => GoTo("stocks")' />
            }
            @if (AppSettings.ShowOpexTab)
            {
                <ListRow Icon="fa-solid fa-file-invoice-dollar" IconClass="si-red" Title="Expenses" Sub="Operating expenses" OnClick='() => GoTo("expenses")' />
            }
            <ListRow Icon="fa-solid fa-file-lines" IconClass="si-cyan" Title="Reports" Sub="Sales trends & exports" OnClick='() => GoTo("reports")' />
            <AuthorizeView Roles="Admin">
                <Authorized>
                    <ListRow Icon="fa-solid fa-shield-halved" IconClass="si-lime" Title="Admin" Sub="Users & invites" OnClick='() => GoTo("admin")' />
                </Authorized>
            </AuthorizeView>
            <ListRow Icon="fa-solid fa-gear" Title="Settings" Sub="App configuration" OnClick='() => GoTo("settings")' />
        </div>
    </BottomSheet>

    <ToastContainer />
    <NotificationPanel @ref="notificationPanel" />
</div>

@if (showOnboarding)
{
    <OnboardingWizard IsVisible="true" OnComplete="OnboardingDone" />
}
```

- [ ] **Step 2: Edit `Layout/MainLayout.razor.cs`**

2a. Delete the field `protected bool showMobileNav = false;` (line ~25).

2b. Add these two fields where `showMobileNav` was:

```csharp
protected bool showQuickSheet = false;
protected bool showMoreSheet = false;
```

2c. Delete the `ToggleMobileNav` and `CloseMobileNav` methods (lines ~44–52):

```csharp
// DELETE THESE:
protected void ToggleMobileNav()
{
    showMobileNav = !showMobileNav;
}

protected void CloseMobileNav()
{
    showMobileNav = false;
}
```

2d. Add these methods in their place:

```csharp
protected void OpenQuickActions()
{
    showMoreSheet = false;
    showQuickSheet = true;
}

protected void OpenMoreSheet()
{
    showQuickSheet = false;
    showMoreSheet = true;
}

protected void GoTo(string page)
{
    showQuickSheet = false;
    showMoreSheet = false;
    NavManager.NavigateTo(page);
}

protected void SetPosView() => AppSettings.PosActiveView = "pos";
protected void SetPosHistoryView() => AppSettings.PosActiveView = "history";
```

2e. Replace the body of `HandleKeyDown` so Escape also closes the sheets (it currently references the deleted `showMobileNav`):

```csharp
protected void HandleKeyDown(KeyboardEventArgs e)
{
    if (e.Key == "Escape")
    {
        showDropdown = false;
        showQuickSheet = false;
        showMoreSheet = false;
    }
}
```

- [ ] **Step 3: Remove obsolete mobile bottom bar CSS from `wwwroot/css/app.css`**

First confirm the old classes are now unused:

Run: `git grep -n "mobile-bottom-bar\|bottom-bar-item\|bottom-nav" -- "*.razor" "*.cs"`
Expected: no matches (the only consumer was MainLayout.razor, replaced in Step 1).

Then delete this block (the `/* ── MOBILE BOTTOM NAV ── */` section, around line 356):

```css
/* ── MOBILE BOTTOM NAV ── */
.bottom-nav,.mobile-bottom-bar{display:none;position:fixed;bottom:0;left:0;right:0;min-height:60px;background:var(--bg2);border-top:1px solid var(--border);z-index:100;align-items:stretch;justify-content:space-around;padding-bottom:env(safe-area-inset-bottom,0px);padding-left:env(safe-area-inset-left,0px);padding-right:env(safe-area-inset-right,0px)}
@media(max-width:767px){.bottom-nav,.mobile-bottom-bar{display:flex}}
.bottom-nav-item,.bottom-bar-item{flex:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:2px;color:var(--fg3);font-size:9px;font-weight:700;cursor:pointer;border:none;background:none;text-transform:uppercase;letter-spacing:.05em;transition:color .12s;text-decoration:none;padding:.5rem 0}
.bottom-nav-item.active,.bottom-bar-item.active{color:var(--lime)}
.bottom-nav-item i,.bottom-bar-item i{font-size:15px}
.bottom-bar-item.disabled{opacity:.4;pointer-events:auto}
```

If the grep DOES return matches outside MainLayout, leave the CSS in place and note it in the commit message instead.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded`. If it fails with `The name 'showMobileNav' does not exist` or `CloseMobileNav`, a reference was missed — re-check Steps 1–2 (the razor file must no longer reference them; note the old `<main class="page-content" @onclick="CloseMobileNav">` is now `<main class="page-content">`).

- [ ] **Step 5: Visual verification**

Run: `dotnet run` (background), open the printed localhost URL.
- At ~390px width (devtools device mode): tab bar shows Home/Sales/[+]/Products/More; FAB opens Quick Actions sheet; More opens More sheet; Escape and overlay-tap close them; navigating from a sheet closes it; no hamburger in the topbar; safe-area padding intact.
- At desktop width: sidebar visible and unchanged; no tab bar; content centered with max-width.
- Toggle light theme: tab bar, sheets, FAB all legible.
Stop the app afterwards.

- [ ] **Step 6: Commit**

```powershell
git add Layout/MainLayout.razor Layout/MainLayout.razor.cs wwwroot/css/app.css
git commit -m "feat: replace mobile nav with native tab bar, FAB quick actions, and More sheet"
```

---

### Task 5: Dashboard redesign

**Files:**
- Modify: `Pages/Dashboard.razor` (full file replacement below)
- Modify: `Pages/Dashboard.razor.cs` (targeted additions below)

Hero greeting header with revenue + trend, segmented time-range control, action buttons, horizontally-scrollable stat cards, list rows instead of tables. The chart/top-products/payments cards keep their existing markup (already responsive).

- [ ] **Step 1: Replace the entire content of `Pages/Dashboard.razor` with:**

```razor
@page "/dashboard"

<PageTitle>Dashboard | @AppSettings.CompanyName</PageTitle>

@if (Inventory.IsLoading && !Inventory.IsLoaded)
{
    <div class="card card-p" style="text-align: center; padding: 3rem;">
        <i class="fa-solid fa-spinner fa-spin" style="font-size: 20px; color: var(--fg3);"></i>
        <p style="color: var(--fg3); margin-top: 12px; font-size: 12px;">Loading dashboard...</p>
    </div>
}
else
{
<div class="section-gap fade-in">

<div class="page-header">
    <div class="hero-head">
        <div class="hero-greet">@Greeting 👋</div>
        <div class="hero-value">
            ₱@AppSettings.FormatCurrency(FilteredRevenue)
            @if (TrendPct is double trend)
            {
                <span class="trend-badge @(trend >= 0 ? "up" : "down")">
                    <i class="fa-solid @(trend >= 0 ? "fa-arrow-trend-up" : "fa-arrow-trend-down")"></i>@Math.Abs(trend).ToString("0")%
                </span>
            }
        </div>
        <div class="hero-label">Revenue · @TimeRangeLabel</div>
    </div>
    <div class="page-header-right">
        <SegmentedControl Options="TimeRangeOptions" @bind-Value="timeRange" />
    </div>
</div>

@* ── Quick Actions ── *@
@if (AppSettings.ShowQuickActions)
{
<div class="action-row">
    <button class="action-btn primary" @onclick='() => Nav.NavigateTo("sales")'><i class="fa-solid fa-plus"></i> New Sale</button>
    @if (AppSettings.ShowInventoryTab)
    {
        <button class="action-btn ghost" @onclick='() => Nav.NavigateTo("stocks")'><i class="fa-solid fa-boxes-stacked"></i> Add Stock</button>
    }
    <button class="action-btn ghost" @onclick='() => Nav.NavigateTo("reports")'><i class="fa-solid fa-file-lines"></i> Reports</button>
</div>
}

@* ── Stats ── *@
@if (AppSettings.ShowTodaySnapshot)
{
<div class="stat-scroll">
    <StatCard Icon="fa-solid fa-chart-pie" IconClass="si-green"
              Value="@($"₱{AppSettings.FormatCurrency(FilteredProfit)}")" Label="Profit" />
    <StatCard Icon="fa-solid fa-receipt" IconClass="si-lime"
              Value="@ActiveTransactionCount.ToString()" Label="Transactions"
              Sub="@($"{FilteredSales.Count()} total incl. voided")" />
    <StatCard Icon="fa-solid fa-scale-balanced" IconClass="si-cyan"
              Value="@($"₱{AppSettings.FormatCurrency(AvgSale)}")" Label="Avg Sale" />
    <StatCard Icon="fa-solid fa-triangle-exclamation" IconClass="si-red"
              Value="@LowStockIngredients.Count.ToString()" Label="Low Stock"
              Sub="items need restocking" />
</div>
}

@* ── Charts ── *@
<div class="grid-2">
    @if (AppSettings.ShowRevenueChart)
    {
    <div class="card card-p">
        <div class="card-title"><i class="fa-solid fa-chart-bar"></i> Revenue Trend</div>
        @if (DailyRevenue.Any())
        {
            <div class="bar-chart">
                @{ var maxRev = DailyRevenue.Max(d => d.Value); if (maxRev == 0) maxRev = 1; }
                @foreach (var day in DailyRevenue)
                {
                    var pct = day.Value / maxRev * 100;
                    <div class="bar-group">
                        <div class="bar bar-lime" style="height: @pct.ToString("0")%;" title="₱@AppSettings.FormatCurrency(day.Value)"></div>
                        <div class="bar-label">@day.Key</div>
                    </div>
                }
            </div>
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-chart-bar" Title="No revenue data" Message="Sales data will appear here." />
        }
    </div>
    }

    @if (AppSettings.ShowProfitTrend)
    {
    <div class="card card-p">
        <div class="card-title"><i class="fa-solid fa-arrow-trend-up"></i> Profit Trend</div>
        @if (DailyProfit.Any())
        {
            <div class="bar-chart">
                @{ var maxProf = DailyProfit.Max(d => Math.Abs(d.Value)); if (maxProf == 0) maxProf = 1; }
                @foreach (var day in DailyProfit)
                {
                    var pct = Math.Abs(day.Value) / maxProf * 100;
                    <div class="bar-group">
                        <div class="bar @(day.Value >= 0 ? "bar-green" : "bar-dim")" style="height: @pct.ToString("0")%;" title="₱@AppSettings.FormatCurrency(day.Value)"></div>
                        <div class="bar-label">@day.Key</div>
                    </div>
                }
            </div>
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-arrow-trend-up" Title="No profit data" Message="Profit trend will appear after sales." />
        }
    </div>
    }
</div>

@* ── Top Products + Payments ── *@
<div class="grid-2">
    @if (AppSettings.ShowTopProducts)
    {
    <div class="card card-p">
        <div class="card-title"><i class="fa-solid fa-trophy"></i> Top Products</div>
        @if (TopProducts.Any())
        {
            var topMax = TopProducts.First().Value; if (topMax == 0) topMax = 1;
            @foreach (var tp in TopProducts)
            {
                var barPct = (double)tp.Value / topMax * 100;
                <div class="progress-row">
                    <div class="progress-label">@tp.Key</div>
                    <div class="progress-track"><div class="progress-fill" style="width: @barPct.ToString("0")%;"></div></div>
                    <div class="progress-value">@tp.Value</div>
                </div>
            }
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-trophy" Title="No sales yet" Message="Top products will appear after your first sale." />
        }
    </div>
    }

    @if (AppSettings.ShowPaymentBreakdown)
    {
    <div class="card card-p">
        <div class="card-title"><i class="fa-solid fa-credit-card"></i> Payments</div>
        @if (PaymentBreakdown.Any())
        {
            var totalPay = PaymentBreakdown.Values.Sum(); if (totalPay == 0) totalPay = 1;
            @foreach (var pm in PaymentBreakdown)
            {
                var pct = pm.Value / totalPay * 100;
                <div class="progress-row">
                    <div class="progress-label">@pm.Key</div>
                    <div class="progress-track"><div class="progress-fill" style="width: @pct.ToString("0")%; background: var(--green);"></div></div>
                    <div class="progress-value" style="font-size: 9px;">₱@AppSettings.FormatCurrency(pm.Value)</div>
                </div>
            }
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-credit-card" Title="No payments yet" Message="Payment breakdown will appear here." />
        }
    </div>
    }
</div>

@* ── Recent Sales + Low Stock ── *@
<div class="grid-2">
    @if (AppSettings.ShowRecentSales)
    {
    <div class="card card-p">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
            <div class="card-title" style="margin-bottom: 0;"><i class="fa-solid fa-receipt"></i> Recent Sales</div>
            <button class="btn btn-ghost btn-sm" @onclick='() => Nav.NavigateTo("reports")'>View all</button>
        </div>
        @if (RecentSales.Any())
        {
            <div>
                @foreach (var sale in RecentSales)
                {
                    <ListRow Flat="true" Icon="fa-solid fa-receipt" IconClass="si-lime"
                             Title="@sale.ProductName"
                             Sub="@($"×{sale.QuantitySold} · {sale.Date:MMM d, h:mm tt}{(sale.IsVoided ? " · VOIDED" : "")}")">
                        <Trailing><span class="text-lime mono">₱@AppSettings.FormatCurrency(sale.TotalAmount)</span></Trailing>
                    </ListRow>
                }
            </div>
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-receipt" Title="No sales yet" Message="Sales will appear here after your first transaction." />
        }
    </div>
    }

    @if (AppSettings.ShowLowStock)
    {
    <div class="card card-p">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
            <div class="card-title" style="margin-bottom: 0;"><i class="fa-solid fa-triangle-exclamation"></i> Low Stock</div>
            <button class="btn btn-ghost btn-sm" @onclick='() => Nav.NavigateTo("stocks")'>Manage</button>
        </div>
        @if (LowStockIngredients.Any())
        {
            <div>
                @foreach (var ing in LowStockIngredients)
                {
                    <ListRow Flat="true" Icon="fa-solid fa-box" IconClass="si-amber"
                             Title="@ing.Name"
                             Sub="@($"{ing.Stock} {ing.Unit} left")">
                        <Trailing>
                            @if (ing.Stock <= 0)
                            {
                                <span class="badge badge-red">Out</span>
                            }
                            else
                            {
                                <span class="badge badge-amber">Low</span>
                            }
                        </Trailing>
                    </ListRow>
                }
            </div>
        }
        else
        {
            <InventoryPlus.Components.EmptyState Icon="fa-solid fa-check-circle" Title="All stocked" Message="No low stock items!" />
        }
    </div>
    }
</div>

</div>
} @* end loading guard *@
```

- [ ] **Step 2: Edit `Pages/Dashboard.razor.cs`**

2a. Add `using InventoryPlus.Components;` to the using block at the top.

2b. Add these members inside the `Dashboard` class (after the existing `FilteredProfit` property is a good spot):

```csharp
protected string Greeting
{
    get
    {
        var h = DateTime.Now.Hour;
        return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
    }
}

protected string TimeRangeLabel => timeRange switch
{
    "today" => "Today",
    "7d" => "Last 7 days",
    "30d" => "Last 30 days",
    _ => "All time"
};

protected List<SegmentedControl.SegOption> TimeRangeOptions => new()
{
    new("today", "1D"),
    new("7d", "7D"),
    new("30d", "30D"),
    new("all", AppSettings.IsPro ? "All" : "All 🔒", !AppSettings.IsPro),
};

protected int ActiveTransactionCount => FilteredSales.Count(s => !s.IsVoided);

protected double AvgSale => ActiveTransactionCount > 0 ? FilteredRevenue / ActiveTransactionCount : 0;

// Revenue change vs the equivalent previous period; null when not comparable
// (all-time range, or no revenue in the previous period).
protected double? TrendPct
{
    get
    {
        var now = DateTime.Now;
        (DateTime Start, DateTime End)? prev = timeRange switch
        {
            "today" => (now.Date.AddDays(-1), now.Date),
            "7d" => (now.AddDays(-14), now.AddDays(-7)),
            "30d" => (now.AddDays(-60), now.AddDays(-30)),
            _ => ((DateTime, DateTime)?)null
        };
        if (prev is not { } p) return null;
        var prevRevenue = Inventory.Sales
            .Where(s => !s.IsVoided && s.Date >= p.Start && s.Date < p.End)
            .Sum(s => s.TotalAmount);
        if (prevRevenue <= 0) return null;
        return (FilteredRevenue - prevRevenue) / prevRevenue * 100;
    }
}
```

Note: the old markup's `@bind:after="Refresh"` on the removed `<select>` is gone; `SegmentedControl`'s `ValueChanged` triggers a re-render on its own, and the `Refresh` method stays (still unused by markup is fine — leave it, the topbar refresh pattern may return in later phases; if the compiler warns, leave as-is, warnings are not errors).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded`. Common failure: `SegOption` not found → ensure `using InventoryPlus.Components;` was added in Step 2a.

- [ ] **Step 4: Visual verification**

Run: `dotnet run`, open the dashboard at ~390px width:
- Greeting + big revenue number + trend badge render; segmented control switches 1D/7D/30D (All locked unless Pro).
- Stat cards scroll horizontally with snap; action buttons are thumb-sized.
- Recent Sales and Low Stock render as icon rows, no horizontal table scrolling anywhere.
- Desktop width: stats become a 4-column grid, header is side-by-side, charts unchanged.
- Both themes legible. Stop the app afterwards.

- [ ] **Step 5: Commit**

```powershell
git add Pages/Dashboard.razor Pages/Dashboard.razor.cs
git commit -m "feat: redesign dashboard with hero header, stat scroller, and list rows"
```

---

### Task 6: Remove dead pending-write queue code from InventoryService

**Files:**
- Modify: `Services/InventoryService.cs`

The queue is write-only dead code: `_pendingWrites` is only ever populated by `LoadPendingQueueAsync` (restoring a queue that nothing ever saves — `SavePendingQueueAsync` has zero callers), and nothing ever flushes it to Supabase. Offline mutations are rejected by design (`SyncSafeAsync` re-throws).

- [ ] **Step 1: Delete the queue declarations (around lines 29–39)**

Delete these lines:

```csharp
private const string PendingKeyPrefix = "inv_pending_";
```

and

```csharp
// Pending writes queue (in-memory; persisted to localStorage)
private readonly Queue<PendingWrite> _pendingWrites = new();

private record PendingWrite(string Op, string Payload, DateTime QueuedAt);
```

- [ ] **Step 2: Delete the restore call inside `LoadAsync` (around lines 64–65)**

Delete:

```csharp
                    // Also restore any unflushed pending writes
                    await LoadPendingQueueAsync(js, userId);
```

- [ ] **Step 3: Delete the entire `// ── Pending Queue ──` region (around lines 692–726)**

Delete from the `// ── Pending Queue ─────...` comment through the closing brace of `PendingWriteDto` (the `LoadPendingQueueAsync` method, `SavePendingQueueAsync` method, and `PendingWriteDto` class), leaving the `// ── Image URL helpers ──` section that follows intact.

- [ ] **Step 4: Verify no references remain and build**

Run: `git grep -n "PendingWrite\|PendingKeyPrefix\|_pendingWrites"`
Expected: no matches.

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```powershell
git add Services/InventoryService.cs
git commit -m "refactor: remove dead offline pending-write queue code"
```

---

### Task 7: Final verification pass

**Files:** none (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build`
Expected: `Build succeeded`

- [ ] **Step 2: Full visual matrix**

Run: `dotnet run` and verify each cell:

| Check | Mobile (~390px) | Desktop (≥1200px) |
|---|---|---|
| Dashboard | hero header, stat scroller, list rows | 4-col stats, centered max-width |
| Tab bar | visible, FAB + sheets work | hidden |
| Sidebar | hidden, no hamburger | visible, nav works |
| Sheets | open/close via tap, overlay, Escape | n/a |
| Dark theme | legible | legible |
| Light theme | legible (FAB/action-btn text white) | legible |
| Feature toggles | turn off ShowInventoryTab + ShowOpexTab in Settings → Stock/Expense entries disappear from FAB + More sheets | same |
| POS mode | enable in Settings → tab bar shows POS/History only | sidebar shows POS nav |

Remember to turn POS mode and the toggles back to their original values afterwards. Stop the app.

- [ ] **Step 3: Report**

Report any failed cell rather than fixing ad-hoc — fixes go through review.

---

## Phase roadmap (later plans, not this one)

- **Phase 2:** Sales/POS — sticky cart bar + cart bottom sheet on mobile (reuses `BottomSheet`), product grid polish, split `Sales.razor` into child components.
- **Phase 3:** Products / Stocks / Expenses / Admin — `ListRow` card lists on mobile, bottom-sheet forms, inline-style extraction.
- **Phase 4:** Reports + Settings (grouped list), auth/landing polish.
