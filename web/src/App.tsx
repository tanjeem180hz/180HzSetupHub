import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import confetti from 'canvas-confetti';
import { Lottie } from 'lottie-react';
import downloadAnim from '../public/lottie/download.json';
import waveAnim from '../public/lottie/wave.json';
import gearsAnim from '../public/lottie/gears.json';
import rocketAnim from '../public/lottie/rocket.json';
import checkPopAnim from '../public/lottie/check_pop.json';
import { 
  Activity, 
  Layers, 
  Trash2, 
  Play, 
  Pause, 
  RotateCcw, 
  CheckCircle2, 
  Sparkles,
  ExternalLink,
  Sliders,
  ShieldCheck,
  Cpu
} from 'lucide-react';
import { 
  easings, 
  staggerContainer, 
  fadeInUp, 
  fadeInScale, 
  interactiveHover, 
  interactiveTap 
} from './design-system/motion';
import { colors } from './design-system/tokens';

interface AppItem {
  id: string;
  name: string;
  category: string;
  size: string;
  icon: string;
  selected: boolean;
}

export default function App() {
  const [activeTab, setActiveTab] = useState<'installer' | 'bandwidth' | 'uninstaller' | 'design'>('installer');
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState(42);
  const [currentSpeed, setCurrentSpeed] = useState(88.4);
  const [peakSpeed, setPeakSpeed] = useState(136.8);
  const [downloadedMB, setDownloadedMB] = useState(384);
  const [copiedToken, setCopiedToken] = useState<string | null>(null);

  const [apps, setApps] = useState<AppItem[]>([
    { id: '1', name: 'Google Chrome', category: 'Browsers', size: '102 MB', icon: '🌐', selected: true },
    { id: '2', name: 'Visual Studio Code', category: 'Development', size: '94 MB', icon: '💻', selected: true },
    { id: '3', name: 'Discord', category: 'Social', size: '88 MB', icon: '💬', selected: true },
    { id: '4', name: 'Steam', category: 'Gaming', size: '2 MB', icon: '🎮', selected: false },
    { id: '5', name: 'Spotify', category: 'Media', size: '78 MB', icon: '🎵', selected: false },
    { id: '6', name: '7-Zip', category: 'Utilities', size: '1.5 MB', icon: '📦', selected: true },
    { id: '7', name: 'OBS Studio', category: 'Streaming', size: '128 MB', icon: '🎥', selected: false },
    { id: '8', name: 'Git for Windows', category: 'Development', size: '54 MB', icon: '🐙', selected: false },
  ]);

  // Real-time speed simulation loop
  useEffect(() => {
    let interval: ReturnType<typeof setInterval>;
    if (isDownloading) {
      interval = setInterval(() => {
        // Random realistic fluctuation between 75 and 140 MB/s
        const jitter = (Math.random() - 0.48) * 12;
        const newSpeed = Math.max(12, Math.min(145, Number((currentSpeed + jitter).toFixed(1))));
        setCurrentSpeed(newSpeed);
        if (newSpeed > peakSpeed) setPeakSpeed(newSpeed);

        setDownloadProgress((prev) => {
          if (prev >= 100) {
            setIsDownloading(false);
            confetti({
              particleCount: 80,
              spread: 70,
              origin: { y: 0.6 },
              colors: ['#00D26A', '#38BDF8', '#818CF8', '#FFFFFF']
            });
            return 100;
          }
          return prev + 1.2;
        });

        setDownloadedMB((prev) => prev + Math.round(newSpeed / 8));
      }, 350);
    } else {
      setCurrentSpeed(0);
    }
    return () => clearInterval(interval);
  }, [isDownloading, currentSpeed, peakSpeed]);

  const toggleSelectApp = (id: string) => {
    setApps(apps.map(a => a.id === id ? { ...a, selected: !a.selected } : a));
  };

  const selectedCount = apps.filter(a => a.selected).length;

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopiedToken(label);
    setTimeout(() => setCopiedToken(null), 1800);
  };

  return (
    <div className="min-h-screen bg-[#050811] text-slate-100 selection:bg-[#00D26A] selection:text-black">
      {/* Dynamic Background Glows */}
      <div className="fixed inset-0 overflow-hidden pointer-events-none z-0">
        <div className="absolute -top-40 left-1/4 w-[500px] h-[500px] bg-[#00D26A]/10 rounded-full blur-[140px]" />
        <div className="absolute top-1/3 -right-40 w-[600px] h-[600px] bg-[#38BDF8]/10 rounded-full blur-[160px]" />
        <div className="absolute -bottom-20 left-1/3 w-[500px] h-[500px] bg-[#818CF8]/10 rounded-full blur-[150px]" />
      </div>

      {/* Top Glass Navigation */}
      <header className="sticky top-0 z-50 glass-panel border-b border-white/5 px-6 py-4">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-[#00D26A] to-[#38BDF8] p-[1px] flex items-center justify-center shadow-lg shadow-[#00D26A]/20">
              <div className="w-full h-full bg-[#050811] rounded-[11px] flex items-center justify-center p-1">
                <Lottie src={gearsAnim} loop autoplay className="w-7 h-7" />
              </div>
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <span className="font-extrabold tracking-tight text-lg text-white">180Hz Setup Hub</span>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-[#00D26A]/15 text-[#00D26A] border border-[#00D26A]/30">PRO</span>
              </div>
              <p className="text-xs text-slate-400">High-Performance Windows Suite</p>
            </div>
          </div>

          <div className="hidden md:flex items-center space-x-1 bg-[#0B132B]/80 p-1.5 rounded-xl border border-white/5">
            {[
              { id: 'installer', label: 'App Setup', icon: Layers },
              { id: 'bandwidth', label: 'Bandwidth Engine', icon: Activity },
              { id: 'uninstaller', label: 'Deep Cleaner', icon: Trash2 },
              { id: 'design', label: 'Motion Tokens', icon: Sliders },
            ].map((tab) => {
              const Icon = tab.icon;
              const isSelected = activeTab === tab.id;
              return (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id as any)}
                  className={`relative flex items-center space-x-2 px-4 py-2 rounded-lg text-xs font-semibold transition-all ${
                    isSelected ? 'text-black' : 'text-slate-400 hover:text-white'
                  }`}
                >
                  {isSelected && (
                    <motion.div
                      layoutId="activeNavTab"
                      className="absolute inset-0 bg-gradient-to-r from-[#00D26A] to-[#38BDF8] rounded-lg"
                      transition={{ type: 'spring', stiffness: 450, damping: 32 }}
                    />
                  )}
                  <span className="relative z-10 flex items-center space-x-2">
                    <Icon className="w-3.5 h-3.5" />
                    <span>{tab.label}</span>
                  </span>
                </button>
              );
            })}
          </div>

          <div className="flex items-center space-x-3">
            <a
              href="https://github.com/tanjeem180hz/180HzSetupHub"
              target="_blank"
              rel="noreferrer"
              className="text-xs text-slate-400 hover:text-white flex items-center space-x-1.5 px-3 py-2 rounded-lg border border-white/5 hover:bg-white/5 transition-all"
            >
              <span>GitHub</span>
              <ExternalLink className="w-3 h-3" />
            </a>
            <motion.a
              href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe"
              whileHover={interactiveHover}
              whileTap={interactiveTap}
              className="flex items-center space-x-2 px-4 py-2 rounded-xl bg-gradient-to-r from-[#00D26A] to-[#38BDF8] text-black font-bold text-xs shadow-lg shadow-[#00D26A]/25 cursor-pointer"
            >
              <div className="w-4 h-4 flex items-center justify-center">
                <Lottie src={rocketAnim} loop autoplay />
              </div>
              <span>Get Installer (2 MB)</span>
            </motion.a>
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="relative z-10 max-w-7xl mx-auto px-6 py-12">
        {/* Hero Section */}
        <motion.div 
          variants={staggerContainer(0.1)}
          initial="hidden"
          animate="visible"
          className="text-center max-w-3xl mx-auto mb-16"
        >
          <motion.div variants={fadeInUp} className="inline-flex items-center space-x-2 px-3.5 py-1.5 rounded-full bg-white/5 border border-white/10 text-xs font-medium text-slate-300 mb-6 backdrop-blur-md">
            <Sparkles className="w-3.5 h-3.5 text-[#00D26A]" />
            <span>Autonomous UI/UX &amp; High-Fidelity Motion Design System</span>
          </motion.div>

          <motion.h1 variants={fadeInUp} className="text-4xl sm:text-6xl font-extrabold tracking-tight text-white mb-6 leading-tight">
            The Ultimate Setup Hub for <br />
            <span className="bg-gradient-to-r from-[#00D26A] via-[#38BDF8] to-[#818CF8] bg-clip-text text-transparent">
              High-Refresh Systems
            </span>
          </motion.h1>

          <motion.p variants={fadeInUp} className="text-slate-400 text-base sm:text-lg leading-relaxed mb-8">
            Engineered with multi-threaded silent install queues, live lifetime bandwidth telemetry, and organic Cubic-Bezier motion physics.
          </motion.p>

          {/* Quick Metrics Bar */}
          <motion.div variants={fadeInUp} className="grid grid-cols-2 sm:grid-cols-4 gap-4 max-w-2xl mx-auto">
            <div className="glass-panel p-4 rounded-2xl text-left border border-white/5">
              <span className="text-[11px] text-slate-400 font-semibold uppercase tracking-wider block">Installer Size</span>
              <span className="text-2xl font-bold text-white mt-1 block">2.0 MB</span>
              <span className="text-[10px] text-[#00D26A] flex items-center gap-1 mt-0.5">⚡ Web Bootstrapper</span>
            </div>
            <div className="glass-panel p-4 rounded-2xl text-left border border-white/5">
              <span className="text-[11px] text-slate-400 font-semibold uppercase tracking-wider block">Peak Speed</span>
              <span className="text-2xl font-bold text-white mt-1 block">{peakSpeed} MB/s</span>
              <span className="text-[10px] text-[#38BDF8] flex items-center gap-1 mt-0.5">🚀 Multi-Threaded</span>
            </div>
            <div className="glass-panel p-4 rounded-2xl text-left border border-white/5">
              <span className="text-[11px] text-slate-400 font-semibold uppercase tracking-wider block">Animation Curve</span>
              <span className="text-2xl font-bold text-white mt-1 block">Quartic</span>
              <span className="text-[10px] text-[#818CF8] flex items-center gap-1 mt-0.5">🎨 Organic Physics</span>
            </div>
            <div className="glass-panel p-4 rounded-2xl text-left border border-white/5">
              <span className="text-[11px] text-slate-400 font-semibold uppercase tracking-wider block">Platform</span>
              <span className="text-2xl font-bold text-white mt-1 block">Win 11/10</span>
              <span className="text-[10px] text-[#00D26A] flex items-center gap-1 mt-0.5">🛡️ Native WPF x64</span>
            </div>
          </motion.div>
        </motion.div>

        {/* Live Interactive Bandwidth Telemetry Card */}
        <motion.div 
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, ease: easings.quarticOut }}
          className="glass-panel-elevated rounded-3xl p-6 sm:p-8 mb-16 border border-cyan-500/20 shadow-2xl shadow-cyan-950/40 relative overflow-hidden"
        >
          <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-6 pb-6 border-b border-white/10">
            <div>
              <div className="flex items-center space-x-3 mb-1">
                <div className="p-1 rounded-lg bg-[#38BDF8]/10 text-[#38BDF8] w-9 h-9 flex items-center justify-center">
                  <Lottie src={isDownloading ? downloadAnim : waveAnim} loop autoplay className="w-7 h-7" />
                </div>
                <div>
                  <h3 className="text-lg font-bold text-white flex items-center gap-2">
                    Live Bandwidth &amp; Download Engine
                    <span className={`w-2 h-2 rounded-full ${isDownloading ? 'bg-[#00D26A] animate-ping' : 'bg-slate-500'}`} />
                  </h3>
                  <p className="text-xs text-slate-400">Dynamic lifetime telemetry with organic wave visualizer</p>
                </div>
              </div>
            </div>

            {/* Interactive Speed Controls */}
            <div className="flex items-center space-x-3 w-full sm:w-auto">
              <motion.button
                whileHover={interactiveHover}
                whileTap={interactiveTap}
                onClick={() => setIsDownloading(!isDownloading)}
                className={`flex-1 sm:flex-none flex items-center justify-center space-x-2 px-5 py-2.5 rounded-xl font-bold text-xs transition-all ${
                  isDownloading 
                    ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30' 
                    : 'bg-[#00D26A] text-black shadow-lg shadow-[#00D26A]/20'
                }`}
              >
                {isDownloading ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                <span>{isDownloading ? 'Pause Stream' : 'Simulate 1 Gbps Download'}</span>
              </motion.button>

              <motion.button
                whileHover={interactiveHover}
                whileTap={interactiveTap}
                onClick={() => {
                  setIsDownloading(false);
                  setDownloadProgress(0);
                  setCurrentSpeed(0);
                  setDownloadedMB(0);
                }}
                className="p-2.5 rounded-xl bg-white/5 hover:bg-white/10 text-slate-300 border border-white/10"
                title="Reset Metrics"
              >
                <RotateCcw className="w-4 h-4" />
              </motion.button>
            </div>
          </div>

          {/* Speed Metrics Display */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6 my-6">
            <div className="bg-[#050811]/60 p-5 rounded-2xl border border-white/5">
              <span className="text-xs text-slate-400 font-medium">Real-Time Transfer Rate</span>
              <div className="flex items-baseline space-x-2 mt-2">
                <span className="text-4xl font-extrabold text-transparent bg-clip-text bg-gradient-to-r from-[#00D26A] to-[#38BDF8]">
                  {currentSpeed.toFixed(1)}
                </span>
                <span className="text-sm font-bold text-slate-300">MB/s</span>
                <span className="text-xs text-slate-500">({(currentSpeed * 8).toFixed(0)} Mbps)</span>
              </div>
            </div>

            <div className="bg-[#050811]/60 p-5 rounded-2xl border border-white/5">
              <span className="text-xs text-slate-400 font-medium">Total Session Transferred</span>
              <div className="flex items-baseline space-x-2 mt-2">
                <span className="text-4xl font-extrabold text-white">
                  {downloadedMB}
                </span>
                <span className="text-sm font-bold text-slate-300">MB</span>
                <span className="text-xs text-[#00D26A]">{(downloadedMB / 1024).toFixed(2)} GB</span>
              </div>
            </div>

            <div className="bg-[#050811]/60 p-5 rounded-2xl border border-white/5">
              <span className="text-xs text-slate-400 font-medium">Active Queue Progress</span>
              <div className="flex items-baseline space-x-2 mt-2">
                <span className="text-4xl font-extrabold text-[#38BDF8]">
                  {Math.round(downloadProgress)}%
                </span>
                <span className="text-xs text-slate-400">
                  {downloadProgress >= 100 ? 'Completed' : isDownloading ? 'Downloading...' : 'Idle'}
                </span>
              </div>
            </div>
          </div>

          {/* Animated Progress Bar */}
          <div className="w-full bg-[#050811] h-3 rounded-full overflow-hidden p-0.5 border border-white/10">
            <motion.div 
              className="h-full bg-gradient-to-r from-[#00D26A] via-[#38BDF8] to-[#818CF8] rounded-full shadow-lg shadow-[#00D26A]/50"
              initial={{ width: 0 }}
              animate={{ width: `${downloadProgress}%` }}
              transition={{ ease: easings.quarticOut, duration: 0.3 }}
            />
          </div>
        </motion.div>

        {/* Tabbed Interactive Feature Demo */}
        <div className="mt-12">
          <AnimatePresence mode="wait">
            {activeTab === 'installer' && (
              <motion.div
                key="installer"
                variants={staggerContainer(0.06)}
                initial="hidden"
                animate="visible"
                exit={{ opacity: 0, y: -10 }}
              >
                <div className="flex items-center justify-between mb-6">
                  <div>
                    <h3 className="text-2xl font-bold text-white">Batch App Installer Queue</h3>
                    <p className="text-xs text-slate-400 mt-1">Select applications to queue silent multi-threaded installation</p>
                  </div>
                  <div className="flex items-center space-x-3">
                    <span className="text-xs font-semibold px-3 py-1.5 rounded-lg bg-white/5 border border-white/10 text-slate-300">
                      {selectedCount} Apps Selected
                    </span>
                    <motion.button
                      whileHover={interactiveHover}
                      whileTap={interactiveTap}
                      onClick={() => {
                        setIsDownloading(true);
                        setDownloadProgress(10);
                      }}
                      className="px-4 py-2 rounded-xl bg-[#00D26A] text-black font-bold text-xs shadow-lg shadow-[#00D26A]/20"
                    >
                      Install Selected ({selectedCount})
                    </motion.button>
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                  {apps.map((app) => (
                    <motion.div
                      key={app.id}
                      variants={fadeInScale}
                      onClick={() => toggleSelectApp(app.id)}
                      whileHover={{ y: -3, transition: { duration: 0.2 } }}
                      className={`p-4 rounded-2xl border transition-all cursor-pointer select-none ${
                        app.selected 
                          ? 'bg-[#111D42]/90 border-[#00D26A]/50 shadow-lg shadow-[#00D26A]/10' 
                          : 'bg-[#0B132B]/60 border-white/5 hover:border-white/20'
                      }`}
                    >
                      <div className="flex items-start justify-between">
                        <div className="w-10 h-10 rounded-xl bg-white/5 flex items-center justify-center text-xl">
                          {app.icon}
                        </div>
                        <div className={`w-6 h-6 rounded-md flex items-center justify-center border transition-all ${
                          app.selected ? 'bg-[#00D26A]/20 border-[#00D26A]' : 'border-slate-600'
                        }`}>
                          {app.selected && <Lottie src={checkPopAnim} loop={false} autoplay className="w-5 h-5" />}
                        </div>
                      </div>
                      <h4 className="font-semibold text-sm text-white mt-3">{app.name}</h4>
                      <div className="flex items-center justify-between text-xs text-slate-400 mt-1">
                        <span>{app.category}</span>
                        <span className="font-mono text-[11px] text-slate-500">{app.size}</span>
                      </div>
                    </motion.div>
                  ))}
                </div>
              </motion.div>
            )}

            {activeTab === 'bandwidth' && (
              <motion.div
                key="bandwidth"
                variants={fadeInUp}
                initial="hidden"
                animate="visible"
                exit={{ opacity: 0, y: -10 }}
                className="glass-panel p-8 rounded-3xl border border-white/10"
              >
                <div className="max-w-xl">
                  <div className="p-3 w-12 h-12 rounded-2xl bg-[#38BDF8]/10 text-[#38BDF8] flex items-center justify-center mb-4">
                    <Activity className="w-6 h-6" />
                  </div>
                  <h3 className="text-2xl font-bold text-white mb-2">Lifetime Dynamic Bandwidth Analytics</h3>
                  <p className="text-slate-400 text-sm leading-relaxed mb-6">
                    Our native bandwidth engine recalculates real-time packet bursts and adapts graph resolution dynamically across KB/s, MB/s, and Gbps scales.
                  </p>
                  <div className="space-y-3">
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-[#00D26A]" />
                      <span>Zero CPU overhead during peak 140+ MB/s saturations</span>
                    </div>
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-[#00D26A]" />
                      <span>Adaptive auto-scaling SVG graph canvas</span>
                    </div>
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-[#00D26A]" />
                      <span>Instant Pause, Resume &amp; Cancel queue synchronizer</span>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}

            {activeTab === 'uninstaller' && (
              <motion.div
                key="uninstaller"
                variants={fadeInUp}
                initial="hidden"
                animate="visible"
                exit={{ opacity: 0, y: -10 }}
                className="glass-panel p-8 rounded-3xl border border-white/10"
              >
                <div className="max-w-xl">
                  <div className="p-3 w-12 h-12 rounded-2xl bg-rose-500/10 text-rose-400 flex items-center justify-center mb-4">
                    <Trash2 className="w-6 h-6" />
                  </div>
                  <h3 className="text-2xl font-bold text-white mb-2">Deep Uninstaller &amp; Clean Registry Purge</h3>
                  <p className="text-slate-400 text-sm leading-relaxed mb-6">
                    Eliminates stub installers, leftovers, registry clutter, and background telemetry services without leaving orphan files.
                  </p>
                  <div className="space-y-3">
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-rose-400" />
                      <span>Force-kill locked process handles before removal</span>
                    </div>
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-rose-400" />
                      <span>Full system scan: 64-bit, 32-bit &amp; AppData stubs</span>
                    </div>
                    <div className="flex items-center space-x-3 text-sm text-slate-300">
                      <CheckCircle2 className="w-4 h-4 text-rose-400" />
                      <span>Export list of installed software to JSON</span>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}

            {activeTab === 'design' && (
              <motion.div
                key="design"
                variants={fadeInUp}
                initial="hidden"
                animate="visible"
                exit={{ opacity: 0, y: -10 }}
                className="space-y-8"
              >
                <div>
                  <h3 className="text-2xl font-bold text-white">Motion Design &amp; Token Architecture</h3>
                  <p className="text-xs text-slate-400 mt-1">Click any token to copy its exact value for implementation</p>
                </div>

                {/* Color Tokens */}
                <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
                  {[
                    { label: 'Void Canvas', hex: colors.void, role: 'Background' },
                    { label: 'Surface Base', hex: colors.surface, role: 'Elevated 1' },
                    { label: 'Neon Mint', hex: colors.accentMint, role: 'Brand Accent' },
                    { label: 'Electric Cyan', hex: colors.accentCyan, role: 'Secondary Glow' },
                    { label: 'Quantum Violet', hex: colors.accentPurple, role: 'Tertiary' },
                    { label: 'System Border', hex: colors.border, role: 'Separators' },
                  ].map((token) => (
                    <motion.div
                      key={token.hex}
                      whileHover={{ y: -3 }}
                      onClick={() => copyToClipboard(token.hex, token.label)}
                      className="glass-panel p-3.5 rounded-2xl border border-white/5 cursor-pointer"
                    >
                      <div className="w-full h-12 rounded-xl mb-3 border border-white/10" style={{ backgroundColor: token.hex }} />
                      <span className="text-xs font-semibold text-white block">{token.label}</span>
                      <span className="text-[10px] font-mono text-slate-400 block mt-0.5">{token.hex}</span>
                      {copiedToken === token.label && (
                        <span className="text-[10px] font-bold text-[#00D26A] mt-1 block">Copied!</span>
                      )}
                    </motion.div>
                  ))}
                </div>

                {/* Easing Curves */}
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  {[
                    { name: 'QuarticOut', curve: 'cubic-bezier(0.16, 1, 0.3, 1)', desc: 'Organic deceleration for sidebar & modals' },
                    { name: 'FluidSpring', curve: 'cubic-bezier(0.34, 1.56, 0.64, 1)', desc: 'Tactile bounce for micro-interactions' },
                    { name: 'SmoothGlide', curve: 'cubic-bezier(0.25, 0.1, 0.25, 1)', desc: 'Subtle slide-in for page enter transitions' },
                  ].map((curve) => (
                    <div key={curve.name} className="glass-panel p-5 rounded-2xl border border-white/5">
                      <span className="text-sm font-bold text-white block">{curve.name}</span>
                      <span className="text-xs font-mono text-[#38BDF8] block mt-1">{curve.curve}</span>
                      <p className="text-xs text-slate-400 mt-2">{curve.desc}</p>
                    </div>
                  ))}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </main>

      {/* Footer */}
      <footer className="border-t border-white/5 py-8 mt-20 text-center text-xs text-slate-500">
        <div className="max-w-7xl mx-auto px-6 flex flex-col sm:flex-row items-center justify-between gap-4">
          <p>© 2026 180Hz Setup Hub. Automated by Google Antigravity Principal UI/UX AI.</p>
          <div className="flex items-center space-x-4">
            <span className="flex items-center gap-1.5 text-slate-400">
              <ShieldCheck className="w-3.5 h-3.5 text-[#00D26A]" /> Clean &amp; Open Source
            </span>
            <span className="flex items-center gap-1.5 text-slate-400">
              <Cpu className="w-3.5 h-3.5 text-[#38BDF8]" /> React 19 + Framer Motion
            </span>
          </div>
        </div>
      </footer>
    </div>
  );
}
