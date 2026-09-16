document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("login-form");
    const email = document.getElementById("email");
    const password = document.getElementById("password");
    const passwordToggle = document.getElementById("password-toggle");
    const submitButton = document.getElementById("login-submit");
    const emailError = document.getElementById("email-error");
    const passwordError = document.getElementById("password-error");

    if (!form || !email || !password || !passwordToggle || !submitButton || !emailError || !passwordError) {
        return;
    }

    const setFieldError = (input, errorElement, message) => {
        errorElement.textContent = message;
        input.setAttribute("aria-invalid", message ? "true" : "false");
    };

    form.addEventListener("submit", (event) => {
        event.preventDefault();

        const isEmailValid = email.validity.valid;
        const isPasswordValid = password.validity.valid;

        setFieldError(email, emailError, isEmailValid ? "" : "請輸入有效的電子郵件。");
        setFieldError(password, passwordError, isPasswordValid ? "" : "請輸入密碼。");

        if (!isEmailValid || !isPasswordValid) {
            (isEmailValid ? password : email).focus();
            return;
        }

        submitButton.disabled = true;
        submitButton.textContent = "登入中…";
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

    form.addEventListener("submit", (event) => {
        event.preventDefault();

        const isEmailValid = email.validity.valid;
        const isPasswordValid = password.validity.valid;
        const isConfirmPasswordValid = confirmPassword.value === password.value && confirmPassword.value !== "";

        setFieldError(email, emailError, isEmailValid ? "" : "請輸入有效的電子郵件。");
        setFieldError(password, passwordError, isPasswordValid ? "" : "密碼至少需要 8 個字元。");
        setFieldError(confirmPassword, confirmPasswordError, isConfirmPasswordValid ? "" : "兩次輸入的密碼不一致。");

        if (!isEmailValid || !isPasswordValid || !isConfirmPasswordValid) {
            (isEmailValid ? (isPasswordValid ? confirmPassword : password) : email).focus();
            return;
        }

        submitButton.disabled = true;
        submitButton.textContent = "註冊中…";
    });

    wireToggle("password-toggle", password);
    wireToggle("confirm-password-toggle", confirmPassword);
});
