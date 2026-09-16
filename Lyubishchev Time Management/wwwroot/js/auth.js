const getCsrfToken = () => document.querySelector('meta[name="csrf-token"]')?.content ?? "";

const showAlert = (element, message) => {
    if (!element) return;
    element.textContent = message;
    element.hidden = !message;
};

const extractErrorMessage = (data, fallback) => {
    if (data?.detail) return data.detail;
    const firstFieldError = Object.values(data?.errors ?? {})[0];
    if (Array.isArray(firstFieldError) && firstFieldError.length > 0) return firstFieldError[0];
    return fallback;
};

const submitAuthRequest = async (url, payload) => {
    const response = await fetch(url, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "X-CSRF-TOKEN": getCsrfToken(),
        },
        body: JSON.stringify(payload),
    });

    let data = null;
    try {
        data = await response.json();
    } catch {
        data = null;
    }

    return { ok: response.ok, data };
};

document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("login-form");
    const email = document.getElementById("email");
    const password = document.getElementById("password");
    const passwordToggle = document.getElementById("password-toggle");
    const submitButton = document.getElementById("login-submit");
    const emailError = document.getElementById("email-error");
    const passwordError = document.getElementById("password-error");
    const loginAlert = document.getElementById("login-alert");

    if (!form || !email || !password || !passwordToggle || !submitButton || !emailError || !passwordError) {
        return;
    }

    const setFieldError = (input, errorElement, message) => {
        errorElement.textContent = message;
        input.setAttribute("aria-invalid", message ? "true" : "false");
    };

    form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const isEmailValid = email.validity.valid;
        const isPasswordValid = password.validity.valid;

        setFieldError(email, emailError, isEmailValid ? "" : "請輸入有效的電子郵件。");
        setFieldError(password, passwordError, isPasswordValid ? "" : "請輸入密碼。");
        showAlert(loginAlert, "");

        if (!isEmailValid || !isPasswordValid) {
            (isEmailValid ? password : email).focus();
            return;
        }

        submitButton.disabled = true;
        submitButton.textContent = "登入中…";

        try {
            const { ok, data } = await submitAuthRequest(`/Account/Login${window.location.search}`, {
                email: email.value,
                password: password.value,
            });

            if (ok && data?.redirectUrl) {
                window.location.href = data.redirectUrl;
                return;
            }

            showAlert(loginAlert, extractErrorMessage(data, "電子郵件或密碼錯誤。"));
        } catch {
            showAlert(loginAlert, "發生錯誤，請稍後再試。");
        } finally {
            submitButton.disabled = false;
            submitButton.textContent = "登入";
        }
    });

    passwordToggle.addEventListener("click", () => {
        const isVisible = password.type === "text";
        password.type = isVisible ? "password" : "text";

        const isNowVisible = password.type === "text";
        passwordToggle.textContent = isNowVisible ? "隱藏" : "顯示";
        passwordToggle.setAttribute("aria-label", isNowVisible ? "隱藏密碼" : "顯示密碼");
        passwordToggle.setAttribute("aria-pressed", String(isNowVisible));
    });
});

document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("register-form");
    const email = document.getElementById("email");
    const password = document.getElementById("password");
    const confirmPassword = document.getElementById("confirm-password");
    const submitButton = document.getElementById("register-submit");
    const emailError = document.getElementById("email-error");
    const passwordError = document.getElementById("password-error");
    const confirmPasswordError = document.getElementById("confirm-password-error");
    const registerAlert = document.getElementById("register-alert");

    if (!form || !email || !password || !confirmPassword || !submitButton || !emailError || !passwordError || !confirmPasswordError) {
        return;
    }

    const setFieldError = (input, errorElement, message) => {
        errorElement.textContent = message;
        input.setAttribute("aria-invalid", message ? "true" : "false");
    };

    const wireToggle = (toggleId, input) => {
        const toggle = document.getElementById(toggleId);
        if (!toggle) return;
        toggle.addEventListener("click", () => {
            const isVisible = input.type === "text";
            input.type = isVisible ? "password" : "text";

            const isNowVisible = input.type === "text";
            toggle.textContent = isNowVisible ? "隱藏" : "顯示";
            toggle.setAttribute("aria-label", isNowVisible ? "隱藏密碼" : "顯示密碼");
            toggle.setAttribute("aria-pressed", String(isNowVisible));
        });
    };

    form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const isEmailValid = email.validity.valid;
        const isPasswordValid = password.validity.valid;
        const isConfirmPasswordValid = confirmPassword.value === password.value && confirmPassword.value !== "";

        setFieldError(email, emailError, isEmailValid ? "" : "請輸入有效的電子郵件。");
        setFieldError(password, passwordError, isPasswordValid ? "" : "密碼至少需要 8 個字元。");
        setFieldError(confirmPassword, confirmPasswordError, isConfirmPasswordValid ? "" : "兩次輸入的密碼不一致。");
        showAlert(registerAlert, "");

        if (!isEmailValid || !isPasswordValid || !isConfirmPasswordValid) {
            (isEmailValid ? (isPasswordValid ? confirmPassword : password) : email).focus();
            return;
        }

        submitButton.disabled = true;
        submitButton.textContent = "註冊中…";

        try {
            const { ok, data } = await submitAuthRequest("/Account/Register", {
                email: email.value,
                password: password.value,
                confirmPassword: confirmPassword.value,
            });

            if (ok && data?.redirectUrl) {
                window.location.href = data.redirectUrl;
                return;
            }

            showAlert(registerAlert, extractErrorMessage(data, "註冊失敗，請稍後再試。"));
        } catch {
            showAlert(registerAlert, "發生錯誤，請稍後再試。");
        } finally {
            submitButton.disabled = false;
            submitButton.textContent = "註冊";
        }
    });

    wireToggle("password-toggle", password);
    wireToggle("confirm-password-toggle", confirmPassword);
});
