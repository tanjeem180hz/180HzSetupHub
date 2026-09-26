using System.Collections.Generic;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public class BundleService
    {
        public List<AppBundle> GetBundles()
        {
            return new List<AppBundle>
            {
                new AppBundle
                {
                    Name = "Gaming Essentials",
                    Description = "Steam, Discord, OBS Studio, and Epic Games.",
                    IconEmoji = "🎮",
                    WingetIds = new List<string>
                    {
                        "Valve.Steam",
                        "Discord.Discord",
                        "OBSProject.OBSStudio",
                        "EpicGames.EpicGamesLauncher"
                    }
                },
                new AppBundle
                {
                    Name = "Developer Kit",
                    Description = "VS Code, Git, Docker Desktop, and Postman.",
                    IconEmoji = "💻",
                    WingetIds = new List<string>
                    {
                        "Microsoft.VisualStudioCode",
                        "Git.Git",
                        "Docker.DockerDesktop",
                        "Postman.Postman"
                    }
                },
                new AppBundle
                {
                    Name = "Everyday Basics",
                    Description = "Google Chrome, Spotify, 7-Zip, and VLC Player.",
                    IconEmoji = "🌟",
                    WingetIds = new List<string>
                    {
                        "Google.Chrome",
                        "Spotify.Spotify",
                        "7zip.7zip",
                        "VideoLAN.VLC"
                    }
                }
            };
        }
    }
}
