/**
 * 180Hz Setup Hub - Design System Tokens
 * Dark Mode Prioritized • Cyber-Modern Ergonomics
 */

export const colors = {
  void: '#050811',         // Ultimate Deep Background
  surface: '#0B132B',      // Base Elevation 1
  surfaceRaised: '#111D42',// Card Elevation 2
  surfaceHover: '#1B2A5E', // Hover & Interactive Surfaces
  border: '#1E293B',       // Subtle Border
  borderHighlight: '#334155', // High-contrast border
  
  // Signature Accents
  accentMint: '#00D26A',   // 180Hz High-Refresh Neon Mint
  accentCyan: '#38BDF8',   // Electric Cyan
  accentPurple: '#818CF8', // Deep Quantum Violet
  
  // Semantic System Colors
  success: '#10B981',
  warning: '#F59E0B',
  danger: '#F43F5E',
  info: '#0EA5E9',

  // High-Contrast Typography
  textPrimary: '#F8FAFC',
  textSecondary: '#94A3B8',
  textMuted: '#64748B',
};

export const typography = {
  fontFamily: {
    sans: "'Segoe UI Variable', system-ui, -apple-system, sans-serif",
    mono: "'JetBrains Mono', 'Fira Code', ui-monospace, monospace",
  },
  display: 'text-4xl sm:text-6xl font-extrabold tracking-tight leading-tight',
  h1: 'text-3xl sm:text-4xl font-bold tracking-tight',
  h2: 'text-2xl sm:text-3xl font-semibold tracking-tight',
  h3: 'text-xl sm:text-2xl font-semibold',
  body: 'text-sm sm:text-base text-slate-300 leading-relaxed',
  caption: 'text-xs uppercase tracking-wider font-semibold text-slate-400',
  mono: 'font-mono text-xs sm:text-sm',
};
