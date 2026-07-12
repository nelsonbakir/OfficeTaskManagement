// Immediate theme initialization to prevent layout flash
(function () {
    const savedTheme = localStorage.getItem('theme');
    // Defaulting to light as requested
    const currentTheme = savedTheme || 'light';
    document.documentElement.setAttribute('data-theme', currentTheme);
})();

// DOM-bound setup for theme toggling
document.addEventListener('DOMContentLoaded', () => {
    const initializeButtons = () => {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'light';
        const buttons = document.querySelectorAll('.theme-toggle-btn');
        
        buttons.forEach(btn => {
            // Update icons inside the button
            const icon = btn.querySelector('i');
            if (icon) {
                // Remove existing classes
                icon.className = '';
                if (currentTheme === 'dark') {
                    icon.className = 'fas fa-sun text-warning';
                } else {
                    icon.className = 'fas fa-moon text-secondary';
                }
            }
        });
    };

    const toggleTheme = () => {
        const currentTheme = document.documentElement.getAttribute('data-theme') || 'light';
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        
        // Update document attribute and save preference
        document.documentElement.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        
        // Synchronize all toggle buttons on the screen
        initializeButtons();
    };

    // Initialize button icons on load
    initializeButtons();

    // Bind click events to all toggle buttons on the page
    document.body.addEventListener('click', (e) => {
        const btn = e.target.closest('.theme-toggle-btn');
        if (btn) {
            e.preventDefault();
            toggleTheme();
        }
    });
});
