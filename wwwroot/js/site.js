/**
 * DuAnCode WMS - Interactive Icon Buttons, Smart Cursor Tooltips, and Abbreviations System
 */
(function () {
    'use strict';

    // -------------------------------------------------------------------------
    // 1. Comprehensive Dictionary of Abbreviations & WMS Terminology
    // -------------------------------------------------------------------------
    const WMS_ABBREVIATIONS = {
        'SKU': {
            full: 'Stock Keeping Unit (Mã đơn vị lưu kho)',
            desc: 'Mã định danh duy nhất cho từng mặt hàng để quản lý và tra cứu chính xác vị trí, số lượng trong kho.'
        },
        'PO': {
            full: 'Purchase Order (Đơn đặt hàng mua)',
            desc: 'Chứng từ đặt mua hàng hóa từ Nhà cung cấp gửi vào hệ thống nhập kho.'
        },
        'SO': {
            full: 'Sales Order (Đơn đặt hàng bán)',
            desc: 'Chứng từ xác nhận đơn hàng xuất bán cho Khách hàng/Đối tác.'
        },
        'ĐVT': {
            full: 'Đơn vị tính (Unit of Measure)',
            desc: 'Đơn vị đo lường quy chuẩn của hàng hóa (Cái, Hộp, Thùng, Kg, Mét, Bộ...).'
        },
        'SL': {
            full: 'Số lượng (Quantity)',
            desc: 'Số lượng sản phẩm ghi nhận trên chứng từ hoặc thực tế kiểm đếm tại kho.'
        },
        'HSD': {
            full: 'Hạn sử dụng (Expiry Date)',
            desc: 'Thời hạn sử dụng của lô sản phẩm - Kho áp dụng xuất kho ưu tiên theo nguyên tắc FEFO.'
        },
        'STT': {
            full: 'Số thứ tự (Index)',
            desc: 'Thứ tự đánh số các dòng hàng hóa trong chứng từ hoặc danh sách.'
        },
        'QC': {
            full: 'Quality Control (Kiểm định chất lượng)',
            desc: 'Quy trình kiểm tra tiêu chuẩn chất lượng hàng hóa khi nhập, xuất hoặc xử lý hàng lỗi hỏng.'
        },
        'WMS': {
            full: 'Warehouse Management System (Hệ thống Quản lý Kho hàng)',
            desc: 'Phần mềm quản trị toàn diện các hoạt động vận hành kho, từ nhập, xuất, chuyển kho đến kiểm kê.'
        },
        'VAT': {
            full: 'Value Added Tax (Thuế Giá trị gia tăng)',
            desc: 'Thuế gián thu tính trên khoản giá trị tăng thêm của hàng hóa, dịch vụ.'
        },
        'FIFO': {
            full: 'First In, First Out (Nhập trước - Xuất trước)',
            desc: 'Nguyên tắc ưu tiên xuất những lô hàng hóa được nhập vào kho sớm nhất.'
        },
        'FEFO': {
            full: 'First Expired, First Out (Hết hạn trước - Xuất trước)',
            desc: 'Nguyên tắc ưu tiên xuất những lô hàng có hạn sử dụng gần nhất để hạn chế quá hạn.'
        },
        'LIFO': {
            full: 'Last In, First Out (Nhập sau - Xuất trước)',
            desc: 'Phương pháp xuất lô hàng mới nhập trước (ít dùng cho hàng tiêu dùng).'
        },
        'ROP': {
            full: 'Reorder Point (Điểm đặt hàng lại)',
            desc: 'Ngưỡng tồn kho an toàn tối thiểu, khi tồn chạm mức này hệ thống AI sẽ cảnh báo đặt thêm hàng.'
        },
        'EOQ': {
            full: 'Economic Order Quantity (Số lượng đặt hàng kinh tế)',
            desc: 'Số lượng hàng tối ưu cần đặt trong mỗi lần nhập để giảm thiểu tổng chi phí lưu kho và đặt hàng.'
        },
        'NCC': {
            full: 'Nhà cung cấp (Supplier / Vendor)',
            desc: 'Đơn vị đối tác cung ứng hàng hóa hoặc nguyên vật liệu cho doanh nghiệp.'
        },
        'KH': {
            full: 'Khách hàng (Customer / Client)',
            desc: 'Đối tác hoặc người mua nhận hàng hóa xuất từ kho.'
        },
        'PN': {
            full: 'Phiếu Nhập kho (Inbound Voucher)',
            desc: 'Chứng từ kế toán và thủ kho ghi nhận hàng hóa chính thức nhập vào kho.'
        },
        'PX': {
            full: 'Phiếu Xuất kho (Outbound Voucher)',
            desc: 'Chứng từ kế toán và thủ kho ghi nhận hàng hóa xuất giao cho đối tác.'
        },
        'PCK': {
            full: 'Phiếu Chuyển kho (Stock Transfer Voucher)',
            desc: 'Chứng từ điều chuyển hàng hóa qua lại giữa các chi nhánh hoặc kho nội bộ.'
        },
        'PKK': {
            full: 'Phiếu Kiểm kê (Inventory Audit Voucher)',
            desc: 'Biên bản kiểm đếm thực tế đối chiếu với số liệu sổ sách phần mềm.'
        },
        'AI': {
            full: 'Artificial Intelligence (Trí tuệ nhân tạo)',
            desc: 'Mô hình học máy phân tích dữ liệu kho, phát hiện bất thường và tự động đề xuất nhập hàng.'
        },
        'RBAC': {
            full: 'Role-Based Access Control (Phân quyền theo vai trò)',
            desc: 'Cơ chế bảo mật phân quyền truy cập chức năng hệ thống theo chức vụ của từng nhân viên.'
        },
        'Admin': {
            full: 'Quản trị viên hệ thống (Administrator)',
            desc: 'Tài khoản có đặc quyền quản trị cao nhất: quản lý tài khoản, phân quyền, cấu hình và sao lưu.'
        },
        'Batch': {
            full: 'Mã số Lô hàng (Batch / Lot Number)',
            desc: 'Mã định danh nhóm hàng cùng một đợt sản xuất, dùng theo dõi nguồn gốc và quản lý hạn sử dụng.'
        },
        'Bin': {
            full: 'Vị trí lưu kho (Bin / Location)',
            desc: 'Tọa độ lưu trữ cụ thể của kiện hàng trong kho (Dãy kệ - Tầng - Ô ngăn).'
        },
        'Location': {
            full: 'Vị trí kho (Warehouse Location)',
            desc: 'Vị trí ngăn ô chứa hàng trong kho nhằm tối ưu quãng đường bốc dỡ và xếp hàng.'
        },
        'Tồn khả dụng': {
            full: 'Tồn kho khả dụng (Available Stock)',
            desc: 'Số lượng hàng thực tế trong kho sẵn sàng để xuất (sau khi trừ đi lượng hàng đã giữ trước cho đơn khác).'
        },
        'Chứng từ gốc': {
            full: 'Chứng từ gốc tham chiếu (Reference Document)',
            desc: 'Số hóa đơn GTGT, hợp đồng hoặc đơn hàng gốc làm căn cứ pháp lý để lập phiếu kho.'
        }
    };

    // -------------------------------------------------------------------------
    // 2. Feature / Function Action Explanations
    // -------------------------------------------------------------------------
    const FEATURE_DESCRIPTIONS = {
        'Tổng quan': 'Dashboard trung tâm tổng hợp các chỉ số KPI kho, biến động xuất - nhập - tồn và biểu đồ thời gian thực.',
        'Nhập kho': 'Quản lý quy trình nhập hàng hóa từ nhà cung cấp hoặc điều chuyển, theo dõi chứng từ gốc và lô hàng.',
        'Xuất kho': 'Quản lý quy trình xuất kho giao hàng đối tác, kiểm tra tồn kho khả dụng theo chuẩn FIFO/FEFO.',
        'Chuyển kho': 'Điều chuyển hàng hóa giữa các kho nội bộ, theo dõi tình trạng kho xuất gửi và kho nhập nhận.',
        'Kiểm kê': 'Lập phiếu kiểm kê thực tế định kỳ, tự động đối chiếu số lượng thực tế với số liệu phần mềm.',
        'Gợi ý AI': 'Thuật toán học máy phân tích tốc độ tiêu thụ, cảnh báo hàng sắp hết và gợi ý điểm đặt hàng ROP/EOQ.',
        'Báo cáo': 'Báo cáo tổng hợp Nhập - Xuất - Tồn, giá trị hàng hóa và lịch sử biến động kho theo kỳ.',
        'Quản lý Hệ thống': 'Cấu hình hệ thống, phân quyền người dùng (RBAC), quản lý nhật ký và sao lưu / phục hồi dữ liệu.',
        'QC Hàng hỏng': 'Kiểm định chất lượng hàng hóa, ghi nhận hàng lỗi hỏng, lập biên bản trả hàng hoặc xử lý phế phẩm.',
        'Tạo Phiếu nhập': 'Khởi tạo phiếu nhập kho mới từ nhà cung cấp hoặc chứng từ PO.',
        'Tạo Phiếu chuyển kho': 'Khởi tạo phiếu điều chuyển hàng hóa giữa các kho nội bộ.',
        'Tạo Phiếu xuất': 'Khởi tạo phiếu xuất kho giao hàng cho đối tác hoặc khách hàng.',
        'Tạo Phiếu Kiểm kê': 'Khởi tạo đợt kiểm kê thực tế kho mới để đối chiếu số lượng.',
        'Chi tiết / Sửa': 'Xem đầy đủ thông tin chi tiết hoặc chỉnh sửa phiếu khi chưa khóa sổ.',
        'In Tem': 'In tem nhãn mã vạch (Barcode / QR Code) cho từng kiện hàng để dán nhãn quét mã kho.',
        'Duyệt Phiếu': 'Phê duyệt chứng từ chính thức: tự động cập nhật số lượng tồn kho tức thì và khóa chỉnh sửa.',
        'Từ chối': 'Từ chối phê duyệt chứng từ và lưu lý do vào nhật ký kiểm toán hệ thống.',
        'Duyệt': 'Chấp thuận đề xuất của AI và tự động sinh phiếu nhập kho bản nháp.',
        'Bỏ qua': 'Bỏ qua cảnh báo/đề xuất này của AI nếu xét thấy chưa cần nhập thêm.',
        'Lọc dữ liệu': 'Lọc và kết xuất dữ liệu báo cáo theo khoảng thời gian và kho hàng chỉ định.',
        'Tạo bản sao lưu ngay': 'Tạo ngay một bản sao lưu toàn bộ dữ liệu hệ thống (JSON snapshot) an toàn.',
        'Chọn': 'Chọn tệp sao lưu này để chuẩn bị khôi phục dữ liệu hệ thống.',
        'Đăng nhập': 'Đăng nhập vào hệ thống quản lý kho DuAnCode WMS.',
        'Đăng xuất': 'Đăng xuất phiên làm việc hiện tại của bạn khỏi hệ thống.',
        'Đã duyệt': 'Chứng từ đã được phê duyệt chính thức, tồn kho đã cập nhật và không thể sửa đổi.',
        'Chờ duyệt': 'Chứng từ đang chờ quản lý kho hoặc giám đốc kiểm tra và phê duyệt.',
        'Từ chối': 'Chứng từ bị từ chối phê duyệt do sai lệch số liệu hoặc không hợp lệ.',
        'Nháp': 'Bản nháp đang soạn thảo, chưa tác động đến số lượng tồn kho thực tế.'
    };

    // -------------------------------------------------------------------------
    // 3. Create or Get Cursor Tooltip Element
    // -------------------------------------------------------------------------
    let tooltipEl = null;

    function getCursorTooltip() {
        if (!tooltipEl) {
            tooltipEl = document.getElementById('wmsCursorTooltip');
            if (!tooltipEl) {
                tooltipEl = document.createElement('div');
                tooltipEl.id = 'wmsCursorTooltip';
                document.body.appendChild(tooltipEl);
            }
        }
        return tooltipEl;
    }

    // -------------------------------------------------------------------------
    // 4. Transform Buttons with Icons into Icon-Only Buttons
    // -------------------------------------------------------------------------
    function processIconButtons(root) {
        const container = root || document;
        // Select all buttons or anchor buttons
        const buttons = container.querySelectorAll('a.btn, button.btn');

        buttons.forEach(btn => {
            // Check if button contains an icon (FontAwesome fa- or SVG)
            const icon = btn.querySelector('i[class*="fa-"], svg');
            if (!icon) return;

            // Collect text nodes directly in button or in immediate non-icon spans
            let directText = '';
            btn.childNodes.forEach(node => {
                if (node.nodeType === Node.TEXT_NODE) {
                    directText += node.textContent.trim() + ' ';
                } else if (node.nodeType === Node.ELEMENT_NODE && !node.matches('i[class*="fa-"], svg') && !node.classList.contains('btn-text-hidden')) {
                    directText += node.textContent.trim() + ' ';
                }
            });

            directText = directText.trim();

            // If button has text along with the icon, convert to icon-only
            if (directText.length > 0) {
                // If button doesn't have data-tooltip, create one from directText or descriptions
                if (!btn.getAttribute('data-tooltip')) {
                    // Match with friendly description if available
                    let explanation = FEATURE_DESCRIPTIONS[directText] || directText;
                    btn.setAttribute('data-tooltip', explanation);
                    btn.setAttribute('data-tt-type', 'action');
                }

                // If native title exists, remove it so browser default doesn't show
                if (btn.hasAttribute('title')) {
                    if (!btn.getAttribute('data-tooltip')) {
                        btn.setAttribute('data-tooltip', btn.getAttribute('title'));
                    }
                    btn.removeAttribute('title');
                }

                // Wrap text nodes in visually-hidden span so screen readers still have access
                btn.childNodes.forEach(node => {
                    if (node.nodeType === Node.TEXT_NODE) {
                        if (node.textContent.trim().length > 0) {
                            const span = document.createElement('span');
                            span.className = 'btn-text-hidden';
                            span.textContent = node.textContent;
                            btn.replaceChild(span, node);
                        }
                    } else if (node.nodeType === Node.ELEMENT_NODE && !node.matches('i[class*="fa-"], svg') && !node.classList.contains('btn-text-hidden')) {
                        node.classList.add('btn-text-hidden');
                    }
                });

                btn.classList.add('btn-icon-only');
            } else {
                // Button already has no text, ensure it has btn-icon-only styling and tooltip
                btn.classList.add('btn-icon-only');
                if (btn.hasAttribute('title') && !btn.getAttribute('data-tooltip')) {
                    btn.setAttribute('data-tooltip', btn.getAttribute('title'));
                    btn.removeAttribute('title');
                }
            }
        });
    }

    // -------------------------------------------------------------------------
    // 5. Automatic Scanning and Tagging of Abbreviations & Features
    // -------------------------------------------------------------------------
    function processAbbreviationsAndFeatures(root) {
        const container = root || document;

        // A. Sidebar nav links
        const navLinks = container.querySelectorAll('.sidebar .nav-link');
        navLinks.forEach(link => {
            const textSpan = link.querySelector('span.text') || link;
            const text = textSpan.textContent.trim();
            if (text && FEATURE_DESCRIPTIONS[text] && !link.getAttribute('data-tooltip')) {
                link.setAttribute('data-tooltip', FEATURE_DESCRIPTIONS[text]);
                link.setAttribute('data-tt-type', 'func');
                link.setAttribute('data-tt-title', text);
            }
        });

        // Specific sidebar elements
        const toggleBtn = document.getElementById('sidebarToggle');
        if (toggleBtn && !toggleBtn.getAttribute('data-tooltip')) {
            toggleBtn.setAttribute('data-tooltip', 'Thu gọn hoặc mở rộng thanh điều hướng bên trái');
            toggleBtn.setAttribute('data-tt-type', 'func');
            toggleBtn.removeAttribute('title');
        }

        const notifBtn = document.getElementById('notifDropdown');
        if (notifBtn && !notifBtn.getAttribute('data-tooltip')) {
            notifBtn.setAttribute('data-tooltip', 'Xem danh sách thông báo hệ thống và cảnh báo tồn kho');
            notifBtn.setAttribute('data-tt-type', 'func');
            notifBtn.removeAttribute('title');
        }

        const chatBtn = document.getElementById('openChatBtn');
        if (chatBtn && !chatBtn.getAttribute('data-tooltip')) {
            chatBtn.setAttribute('data-tooltip', 'Mở Trợ lý AI Ollama hỗ trợ tra cứu thông tin kho và hỏi đáp thông minh');
            chatBtn.setAttribute('data-tt-type', 'func');
        }

        const sendBtn = document.getElementById('sendChatBtn');
        if (sendBtn && !sendBtn.getAttribute('data-tooltip')) {
            sendBtn.setAttribute('data-tooltip', 'Gửi câu hỏi tới Trợ lý AI');
            sendBtn.setAttribute('data-tt-type', 'action');
        }

        // B. Status badges in tables
        const badges = container.querySelectorAll('.badge');
        badges.forEach(badge => {
            const text = badge.textContent.trim();
            if (FEATURE_DESCRIPTIONS[text] && !badge.getAttribute('data-tooltip')) {
                badge.setAttribute('data-tooltip', FEATURE_DESCRIPTIONS[text]);
                badge.setAttribute('data-tt-type', 'status');
                badge.setAttribute('data-tt-title', text);
            }
        });

        // B2. Direct data-abbr elements (e.g. <i class="help-icon" data-abbr="SKU"></i>)
        const abbrEls = container.querySelectorAll('[data-abbr]');
        abbrEls.forEach(el => {
            const key = el.getAttribute('data-abbr');
            if (key && WMS_ABBREVIATIONS[key]) {
                if (!el.getAttribute('data-tooltip')) {
                    el.setAttribute('data-tooltip', WMS_ABBREVIATIONS[key].full + ': ' + WMS_ABBREVIATIONS[key].desc);
                }
                el.setAttribute('data-tt-type', 'abbr');
                el.setAttribute('data-tt-title', key);
            }
        });

        // B3. Ensure all .help-icon elements have interactive button attributes
        const helpIcons = container.querySelectorAll('.help-icon');
        helpIcons.forEach(icon => {
            icon.setAttribute('role', 'button');
            icon.setAttribute('tabindex', '0');
            if (!icon.getAttribute('aria-label')) {
                icon.setAttribute('aria-label', 'Bấm để xem giải thích chi tiết');
            }
            if (!icon.getAttribute('data-tt-title')) {
                const parent = icon.closest('label, th, .card-title, td');
                if (parent) {
                    const clone = parent.cloneNode(true);
                    clone.querySelectorAll('.help-icon, i, svg, .text-danger, .badge').forEach(n => n.remove());
                    const txt = clone.textContent.replace(/[*:\(\)]/g, ' ').replace(/\s+/g, ' ').trim();
                    if (txt) icon.setAttribute('data-tt-title', txt);
                }
            }
        });

        // C. Table headers `<th>` and form labels `<label>` for Abbreviations
        const targets = container.querySelectorAll('th, label, .card-title, td.fw-bold');
        const abbrKeys = Object.keys(WMS_ABBREVIATIONS);

        targets.forEach(el => {
            if (el.getAttribute('data-tooltip')) return;

            const text = el.textContent.trim();
            for (let i = 0; i < abbrKeys.length; i++) {
                const key = abbrKeys[i];
                // Check exact word match or contained in parenthesis like "(Mã SKU)" or "Mã SKU" or "ĐVT"
                const regex = new RegExp(`(^|\\s|\\(|/)${key}($|\\s|\\)|/)`, 'i');
                if (regex.test(text)) {
                    el.classList.add('wms-abbr');
                    el.setAttribute('data-tooltip', WMS_ABBREVIATIONS[key].full + ': ' + WMS_ABBREVIATIONS[key].desc);
                    el.setAttribute('data-tt-type', 'abbr');
                    el.setAttribute('data-tt-title', key);
                    el.setAttribute('role', 'button');
                    el.setAttribute('tabindex', '0');
                    break;
                }
            }
        });

        // Clean up any remaining native titles to prevent native browser tooltip clash
        const titledEls = container.querySelectorAll('[title]');
        titledEls.forEach(el => {
            const title = el.getAttribute('title');
            if (title && title.trim().length > 0) {
                if (!el.getAttribute('data-tooltip')) {
                    el.setAttribute('data-tooltip', title.trim());
                }
                el.removeAttribute('title');
            }
        });
    }

    // -------------------------------------------------------------------------
    // 6. Interactive Explanation Popover (Click / Tap to Open)
    // -------------------------------------------------------------------------
    let activePopoverTarget = null;
    let popoverEl = null;

    function getExplanationPopover() {
        if (!popoverEl) {
            popoverEl = document.getElementById('wmsExplanationPopover');
            if (!popoverEl) {
                popoverEl = document.createElement('div');
                popoverEl.id = 'wmsExplanationPopover';
                popoverEl.className = 'wms-popover';
                popoverEl.innerHTML = `
                    <div class="wms-popover-arrow"></div>
                    <div class="wms-popover-header">
                        <span class="wms-popover-badge"></span>
                        <button type="button" class="wms-popover-close" aria-label="Đóng">&times;</button>
                    </div>
                    <div class="wms-popover-body">
                        <div class="wms-popover-title"></div>
                        <div class="wms-popover-subtitle"></div>
                        <div class="wms-popover-desc"></div>
                        <div class="wms-popover-tip" style="display: none;"></div>
                    </div>
                    <div class="wms-popover-footer">
                        <button type="button" class="wms-popover-ok-btn"><i class="fa-solid fa-check me-1"></i>Đã hiểu</button>
                    </div>
                `;
                document.body.appendChild(popoverEl);
            }
        }
        return popoverEl;
    }

    function extractFieldTitle(target) {
        if (target.getAttribute('data-tt-title')) return target.getAttribute('data-tt-title');
        const parent = target.closest('label, th, .card-title, td');
        if (!parent) return 'Thông tin trường';
        const clone = parent.cloneNode(true);
        clone.querySelectorAll('.help-icon, i, svg, .text-danger, .badge').forEach(n => n.remove());
        const txt = clone.textContent.replace(/[*:\(\)]/g, ' ').replace(/\s+/g, ' ').trim();
        return txt.length > 0 ? txt : 'Thông tin chi tiết';
    }

    function showExplanationPopover(target) {
        if (!target) return;

        // Hide hover tooltip so they don't overlap
        const hoverTooltip = getCursorTooltip();
        if (hoverTooltip) hoverTooltip.classList.remove('tooltip-visible');
        currentTooltipTarget = null;

        // If clicking same target, toggle close
        if (activePopoverTarget === target) {
            hideExplanationPopover();
            return;
        }

        // Clean previous target active state
        if (activePopoverTarget) {
            activePopoverTarget.classList.remove('help-icon-active');
        }

        activePopoverTarget = target;
        target.classList.add('help-icon-active');

        const popover = getExplanationPopover();

        // Extract content
        let title = '';
        let subtitle = '';
        let desc = target.getAttribute('data-tooltip') || '';
        let type = target.getAttribute('data-tt-type') || 'field';
        let badgeText = 'Trường thông tin';
        let badgeIcon = 'fa-circle-info';
        let badgeClass = 'tag-field';
        let tipText = '';

        if (target.hasAttribute('data-abbr')) {
            const key = target.getAttribute('data-abbr');
            const info = WMS_ABBREVIATIONS[key];
            title = key;
            subtitle = info ? info.full : '';
            desc = info ? info.desc : desc;
            type = 'abbr';
            badgeText = 'Thuật ngữ WMS';
            badgeIcon = 'fa-book-bookmark';
            badgeClass = 'tag-abbr';

            if (key === 'FIFO' || key === 'FEFO' || key === 'Batch' || key === 'HSD') {
                tipText = '💡 <strong>Gợi ý WMS:</strong> Kho tự động áp dụng FEFO/FIFO ưu tiên xuất các lô hàng nhập trước hoặc có hạn sử dụng gần nhất.';
            } else if (key === 'ROP' || key === 'EOQ') {
                tipText = '💡 <strong>Trợ lý AI:</strong> Hệ thống tự động phân tích biến động xuất nhập để đề xuất ngưỡng đặt hàng tối ưu.';
            }
        } else {
            title = extractFieldTitle(target);

            if (type === 'func') {
                badgeText = 'Chức năng WMS';
                badgeIcon = 'fa-cube';
                badgeClass = 'tag-func';
            } else if (type === 'status') {
                badgeText = 'Trạng thái';
                badgeIcon = 'fa-circle-check';
                badgeClass = 'tag-status';
            } else if (type === 'action') {
                badgeText = 'Thao tác';
                badgeIcon = 'fa-bolt';
                badgeClass = 'tag-abbr';
            } else {
                badgeText = 'Trường nhập liệu';
                badgeIcon = 'fa-pen-to-square';
                badgeClass = 'tag-field';
            }

            // Inferred business tips
            const lowerTitle = title.toLowerCase();
            if (lowerTitle.includes('kho nguồn') || lowerTitle.includes('kho đích') || lowerTitle.includes('kho tiếp nhận') || lowerTitle.includes('kho xuất')) {
                tipText = '📦 <strong>Lưu ý:</strong> Tồn kho thực tế sẽ được cập nhật tự động ngay khi phiếu được cấp quản lý phê duyệt chính thức.';
            } else if (lowerTitle.includes('chênh lệch') || lowerTitle.includes('kiểm kê')) {
                tipText = '⚖️ <strong>Cân bằng kho:</strong> Sau khi duyệt biên bản kiểm kê, hệ thống sẽ tự động điều chỉnh số lượng tồn khớp với thực tế.';
            } else if (lowerTitle.includes('lô') || lowerTitle.includes('hạn sử dụng')) {
                tipText = '⏱️ <strong>Nguyên tắc kho:</strong> Hàng hóa có hạn dùng gần nhất sẽ được ưu tiên chọn trước khi xuất kho.';
            }
        }

        // Render Popover UI
        const badgeEl = popover.querySelector('.wms-popover-badge');
        badgeEl.className = `wms-popover-badge ${badgeClass}`;
        badgeEl.innerHTML = `<i class="fa-solid ${badgeIcon}"></i> ${badgeText}`;

        const titleEl = popover.querySelector('.wms-popover-title');
        titleEl.textContent = title;

        const subEl = popover.querySelector('.wms-popover-subtitle');
        if (subtitle && subtitle !== title) {
            subEl.textContent = subtitle;
            subEl.style.display = 'block';
        } else {
            subEl.style.display = 'none';
        }

        const descEl = popover.querySelector('.wms-popover-desc');
        descEl.textContent = desc;

        const tipEl = popover.querySelector('.wms-popover-tip');
        if (tipText) {
            tipEl.innerHTML = tipText;
            tipEl.style.display = 'block';
        } else {
            tipEl.style.display = 'none';
        }

        // Position popover relative to target
        positionPopover(popover, target);
    }

    function positionPopover(popover, target) {
        popover.style.display = 'block';
        popover.style.visibility = 'hidden';

        const rect = target.getBoundingClientRect();
        const popRect = popover.getBoundingClientRect();
        const scrollX = window.pageXOffset || document.documentElement.scrollLeft;
        const scrollY = window.pageYOffset || document.documentElement.scrollTop;

        const winW = window.innerWidth;
        const winH = window.innerHeight;

        let placement = 'bottom';
        let top = rect.bottom + scrollY + 8;

        // If bottom overflows viewport, place above target
        if (rect.bottom + popRect.height + 20 > winH && rect.top - popRect.height - 20 > 0) {
            placement = 'top';
            top = rect.top + scrollY - popRect.height - 8;
        }

        // Horizontally center above target
        let left = rect.left + scrollX + (rect.width / 2) - (popRect.width / 2);
        const padding = 12;

        if (left < padding) {
            left = padding;
        } else if (left + popRect.width > winW - padding) {
            left = winW - popRect.width - padding;
        }

        // Arrow position pointing at target center
        const targetCenterX = rect.left + (rect.width / 2);
        let arrowLeft = targetCenterX - (left - scrollX);
        if (arrowLeft < 16) arrowLeft = 16;
        if (arrowLeft > popRect.width - 16) arrowLeft = popRect.width - 16;

        popover.className = `wms-popover wms-popover-${placement}`;
        popover.style.top = `${Math.round(top)}px`;
        popover.style.left = `${Math.round(left)}px`;
        popover.style.visibility = 'visible';

        const arrowEl = popover.querySelector('.wms-popover-arrow');
        if (arrowEl) {
            arrowEl.style.left = `${Math.round(arrowLeft)}px`;
        }

        // Trigger animation
        requestAnimationFrame(() => {
            popover.classList.add('wms-popover-visible');
        });
    }

    function hideExplanationPopover() {
        if (!popoverEl) return;
        popoverEl.classList.remove('wms-popover-visible');
        setTimeout(() => {
            if (!popoverEl.classList.contains('wms-popover-visible')) {
                popoverEl.style.display = 'none';
            }
        }, 180);

        if (activePopoverTarget) {
            activePopoverTarget.classList.remove('help-icon-active');
            activePopoverTarget = null;
        }
    }

    // -------------------------------------------------------------------------
    // 7. Smart Cursor-Following Tooltip Events (Hover Preview)
    // -------------------------------------------------------------------------
    let currentTooltipTarget = null;

    function renderTooltipContent(target) {
        const text = target.getAttribute('data-tooltip') || '';
        const type = target.getAttribute('data-tt-type') || '';
        const title = target.getAttribute('data-tt-title') || '';

        let badgeHtml = '';
        if (type === 'abbr') {
            badgeHtml = `<span class="wms-tt-badge tag-abbr"><i class="fa-solid fa-book-bookmark"></i> ${title || 'Từ viết tắt'}</span>`;
        } else if (type === 'func') {
            badgeHtml = `<span class="wms-tt-badge tag-func"><i class="fa-solid fa-cube"></i> ${title || 'Chức năng'}</span>`;
        } else if (type === 'status') {
            badgeHtml = `<span class="wms-tt-badge tag-status"><i class="fa-solid fa-circle-info"></i> ${title || 'Trạng thái'}</span>`;
        } else if (type === 'action') {
            badgeHtml = `<span class="wms-tt-badge tag-action"><i class="fa-solid fa-bolt"></i> Thao tác</span>`;
        }

        return `${badgeHtml}<span class="wms-tt-content">${text}</span>`;
    }

    function updateTooltipPosition(e) {
        const tooltip = getCursorTooltip();
        if (!tooltip || !tooltip.classList.contains('tooltip-visible')) return;

        const offset = 14;
        let x = e.clientX + offset;
        let y = e.clientY + offset;

        const rect = tooltip.getBoundingClientRect();
        const winW = window.innerWidth;
        const winH = window.innerHeight;

        // If overflowing right boundary, position to the left of cursor
        if (x + rect.width > winW - 12) {
            x = e.clientX - rect.width - 12;
        }

        // If overflowing bottom boundary, position above cursor
        if (y + rect.height > winH - 12) {
            y = e.clientY - rect.height - 12;
        }

        // Keep inside screen
        if (x < 8) x = 8;
        if (y < 8) y = 8;

        tooltip.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`;
    }

    document.addEventListener('mouseover', function (e) {
        // If an explanation popover is active, do not display hover tooltip
        if (activePopoverTarget) return;

        const target = e.target.closest('[data-tooltip], [title], [data-abbr]');
        if (!target) return;

        // Convert data-abbr if tooltip not yet initialized
        if (target.hasAttribute('data-abbr') && !target.getAttribute('data-tooltip')) {
            const key = target.getAttribute('data-abbr');
            if (key && WMS_ABBREVIATIONS[key]) {
                target.setAttribute('data-tooltip', WMS_ABBREVIATIONS[key].full + ': ' + WMS_ABBREVIATIONS[key].desc);
                target.setAttribute('data-tt-type', 'abbr');
                target.setAttribute('data-tt-title', key);
            }
        }

        // Convert title if present
        if (target.hasAttribute('title')) {
            const t = target.getAttribute('title');
            if (t) target.setAttribute('data-tooltip', t);
            target.removeAttribute('title');
        }

        const tooltipText = target.getAttribute('data-tooltip');
        if (!tooltipText || tooltipText.trim().length === 0) return;

        currentTooltipTarget = target;
        const tooltip = getCursorTooltip();
        tooltip.innerHTML = renderTooltipContent(target);
        tooltip.classList.add('tooltip-visible');

        updateTooltipPosition(e);
    });

    document.addEventListener('mousemove', function (e) {
        if (currentTooltipTarget && !activePopoverTarget) {
            updateTooltipPosition(e);
        }
    });

    document.addEventListener('mouseout', function (e) {
        if (!currentTooltipTarget) return;

        const related = e.relatedTarget;
        if (!currentTooltipTarget.contains(related)) {
            const tooltip = getCursorTooltip();
            if (tooltip) {
                tooltip.classList.remove('tooltip-visible');
            }
            currentTooltipTarget = null;
        }
    });

    // -------------------------------------------------------------------------
    // 8. Global Click & Keyboard Events (Opening / Closing Popovers & Tooltips)
    // -------------------------------------------------------------------------
    document.addEventListener('click', function (e) {
        // 1. Check if user clicked on a help icon, abbreviation, or question element
        const helpTarget = e.target.closest('.help-icon, [data-abbr], .wms-abbr');
        const popover = document.getElementById('wmsExplanationPopover');

        if (helpTarget) {
            e.preventDefault();
            e.stopPropagation();
            if (activePopoverTarget === helpTarget) {
                hideExplanationPopover();
            } else {
                showExplanationPopover(helpTarget);
            }
            return;
        }

        // 2. Check if click was inside popover
        if (popover && popover.contains(e.target)) {
            if (e.target.closest('.wms-popover-close, .wms-popover-ok-btn')) {
                hideExplanationPopover();
            }
            return;
        }

        // 3. Clicked outside: close active popover
        if (activePopoverTarget) {
            hideExplanationPopover();
        }

        // Hide hover tooltip
        if (currentTooltipTarget) {
            const tooltip = getCursorTooltip();
            if (tooltip) tooltip.classList.remove('tooltip-visible');
            currentTooltipTarget = null;
        }
    });

    // Keyboard support: Escape closes, Enter/Space on focused help-icon triggers click
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            hideExplanationPopover();
            const tooltip = getCursorTooltip();
            if (tooltip) tooltip.classList.remove('tooltip-visible');
            currentTooltipTarget = null;
        } else if (e.key === 'Enter' || e.key === ' ') {
            if (document.activeElement && document.activeElement.classList.contains('help-icon')) {
                e.preventDefault();
                document.activeElement.click();
            }
        }
    });

    // Hide tooltip / reposition popover on scroll or resize
    window.addEventListener('scroll', function () {
        if (currentTooltipTarget) {
            const tooltip = getCursorTooltip();
            if (tooltip) tooltip.classList.remove('tooltip-visible');
            currentTooltipTarget = null;
        }
        if (activePopoverTarget && popoverEl && popoverEl.classList.contains('wms-popover-visible')) {
            positionPopover(popoverEl, activePopoverTarget);
        }
    }, { passive: true });

    window.addEventListener('resize', function () {
        if (activePopoverTarget && popoverEl && popoverEl.classList.contains('wms-popover-visible')) {
            positionPopover(popoverEl, activePopoverTarget);
        }
    }, { passive: true });

    // -------------------------------------------------------------------------
    // 9. Initialization & Mutation Observer for Dynamic Content
    // -------------------------------------------------------------------------
    function init() {
        processIconButtons();
        processAbbreviationsAndFeatures();

        // Observe DOM changes (e.g., dynamically rendered items, AJAX loads)
        const observer = new MutationObserver(mutations => {
            let hasNewElements = false;
            mutations.forEach(m => {
                if (m.addedNodes.length > 0) hasNewElements = true;
            });
            if (hasNewElements) {
                processIconButtons();
                processAbbreviationsAndFeatures();
            }
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();

