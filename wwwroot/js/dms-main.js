/**
 * Dairy Management System - Frontend Foundation Script
 * Handles sidebar toggling, responsive overlays, password visibility toggles,
 * form submission loading states, and helper behaviors.
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

    // Password Visibility Toggle Handler
    const passwordToggleButtons = document.querySelectorAll('.btn-password-toggle');
    passwordToggleButtons.forEach(function (toggleBtn) {
        toggleBtn.addEventListener('click', function (e) {
            e.preventDefault();
            const targetId = this.getAttribute('data-target');
            if (!targetId) return;

            const passwordInput = document.getElementById(targetId);
            if (!passwordInput) return;

            const isPassword = passwordInput.type === 'password';
            passwordInput.type = isPassword ? 'text' : 'password';

            const iconShow = this.querySelector('.icon-show');
            const iconHide = this.querySelector('.icon-hide');

            if (iconShow && iconHide) {
                if (isPassword) {
                    iconShow.classList.add('d-none');
                    iconHide.classList.remove('d-none');
                    this.setAttribute('title', 'Hide password');
                    this.setAttribute('aria-label', 'Hide password');
                } else {
                    iconShow.classList.remove('d-none');
                    iconHide.classList.add('d-none');
                    this.setAttribute('title', 'Show password');
                    this.setAttribute('aria-label', 'Show password');
                }
            }
        });
    });

    // Login Form Submission Loading State Helper
    const loginForms = document.querySelectorAll('#adminLoginForm, #deliveryBoyLoginForm');
    loginForms.forEach(function (form) {
        form.addEventListener('submit', function (e) {
            // If jQuery Validation is active, check validity before activating spinner
            if (window.jQuery && typeof window.jQuery(form).valid === 'function') {
                if (!window.jQuery(form).valid()) {
                    return; // Client-side validation failed; do not enter loading state
                }
            }

            const submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn) {
                const btnText = submitBtn.querySelector('.btn-text');
                const btnSpinner = submitBtn.querySelector('.btn-spinner');

                if (btnText && btnSpinner) {
                    btnText.classList.add('d-none');
                    btnSpinner.classList.remove('d-none');
                }

                // Prevent double submissions while keeping form data intact
                submitBtn.disabled = true;

                // Safety timeout to restore button state if navigation takes unusually long
                setTimeout(function () {
                    if (submitBtn.disabled) {
                        submitBtn.disabled = false;
                        if (btnText && btnSpinner) {
                            btnText.classList.remove('d-none');
                            btnSpinner.classList.add('d-none');
                        }
                    }
                }, 8000);
            }
        });
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
