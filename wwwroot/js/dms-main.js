/**
 * Dairy Management System - Frontend Foundation Script
 * Handles sidebar toggling, responsive overlays, and helper behaviors
 */

document.addEventListener('DOMContentLoaded', function () {
    // Universal sidebar toggle helper
    const sidebar = document.getElementById('sidebar');
    const togglerButtons = document.querySelectorAll('[data-dms-toggle="sidebar"], .header-toggler');

    togglerButtons.forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            if (sidebar) {
                if (window.coreui && coreui.Sidebar) {
                    const instance = coreui.Sidebar.getOrCreateInstance(sidebar);
                    instance.toggle();
                } else {
                    sidebar.classList.toggle('show');
                }
            }
        });
    });

    // Close mobile sidebar on click outside
    document.addEventListener('click', function (e) {
        if (window.innerWidth < 992 && sidebar && (sidebar.classList.contains('show') || sidebar.classList.contains('sidebar-show'))) {
            const isInsideSidebar = sidebar.contains(e.target);
            let isInsideToggler = false;
            togglerButtons.forEach(btn => {
                if (btn.contains(e.target)) isInsideToggler = true;
            });
            if (!isInsideSidebar && !isInsideToggler) {
                if (window.coreui && coreui.Sidebar) {
                    coreui.Sidebar.getOrCreateInstance(sidebar).hide();
                } else {
                    sidebar.classList.remove('show');
                    sidebar.classList.remove('sidebar-show');
                }
            }
        }
    });

    // Quick quantity picker helper for delivery boy entry
    const qtyInput = document.getElementById('milkQuantityInput');
    const qtyButtons = document.querySelectorAll('.quick-qty-btn');
    if (qtyInput && qtyButtons.length > 0) {
        qtyButtons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                if (qtyInput.disabled) return;
                const addVal = parseFloat(this.getAttribute('data-add-qty') || '0');
                const currentVal = parseFloat(qtyInput.value) || 0;
                const newVal = Math.max(0, currentVal + addVal);
                qtyInput.value = newVal.toFixed(2);
                qtyInput.dispatchEvent(new Event('change'));
            });
        });
    }

    // Dismissable alerts auto-fade helper
    const alerts = document.querySelectorAll('.alert-dismissible');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            if (alert && alert.classList.contains('show')) {
                if (window.coreui && coreui.Alert) {
                    coreui.Alert.getOrCreateInstance(alert).close();
                } else {
                    alert.classList.remove('show');
                }
            }
        }, 5000);
    });
});
