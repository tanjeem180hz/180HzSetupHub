import { type Variants } from 'framer-motion';

/**
 * 180Hz Setup Hub - High-Fidelity Motion Design System
 * Inspired by Linear, macOS Sonoma, and Windows 11 Fluent 2
 */

// Custom Cubic-Bezier Easing Constants
export const easings = {
  // Ultra-smooth deceleration (snappy start, gradual settle)
  quarticOut: [0.16, 1, 0.3, 1] as const,
  // Tactile organic spring physics
  fluidSpring: [0.34, 1.56, 0.64, 1] as const,
  // Buttery content reveal glide
  smoothGlide: [0.25, 0.1, 0.25, 1] as const,
  // Crisp card / modal popup
  snappyEntrance: [0.22, 1, 0.36, 1] as const,
  // Dramatic anticipation curve
  anticipate: [0.68, -0.6, 0.32, 1.6] as const,
};

// Container Stagger Variants
export const staggerContainer = (staggerDelay = 0.08, delayChildren = 0.05): Variants => ({
  hidden: { opacity: 0 },
  visible: {
    opacity: 1,
    transition: {
      staggerChildren: staggerDelay,
      delayChildren,
    },
  },
});

// Smooth Fade + Slide In Variants
export const fadeInUp: Variants = {
  hidden: { opacity: 0, y: 18 },
  visible: {
    opacity: 1,
    y: 0,
    transition: {
      duration: 0.45,
      ease: easings.quarticOut,
    },
  },
};

export const fadeInScale: Variants = {
  hidden: { opacity: 0, scale: 0.94, y: 10 },
  visible: {
    opacity: 1,
    scale: 1,
    y: 0,
    transition: {
      duration: 0.4,
      ease: easings.fluidSpring,
    },
  },
};

export const slideInLeft: Variants = {
  hidden: { opacity: 0, x: -16 },
  visible: {
    opacity: 1,
    x: 0,
    transition: {
      duration: 0.35,
      ease: easings.quarticOut,
    },
  },
};

// Micro-Interaction Hover & Active Presets
export const interactiveHover = {
  scale: 1.025,
  y: -2,
  transition: {
    duration: 0.2,
    ease: easings.quarticOut,
  },
};

export const interactiveTap = {
  scale: 0.965,
  transition: {
    duration: 0.1,
  },
};

export const glowHover = (color = 'rgba(0, 210, 106, 0.35)') => ({
  boxShadow: `0 0 25px ${color}`,
  borderColor: 'rgba(0, 210, 106, 0.8)',
  transition: { duration: 0.25 },
});
