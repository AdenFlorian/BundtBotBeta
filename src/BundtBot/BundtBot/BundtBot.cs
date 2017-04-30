﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BundtBot.Youtube;
using BundtCommon;
using BundtCord.Discord;
using DiscordApiWrapper.Audio;
using Newtonsoft.Json;

namespace BundtBot
{
    public class BundtBot
    {
        static readonly MyLogger _logger = new MyLogger(nameof(BundtBot));

        DiscordClient _client;
        DJ _dj = new DJ();
        CommandManager _commandManager = new CommandManager();
        YoutubeDl _youtubeDl;

        public async Task StartAsync()
        {
            _client = SetupDiscordClient();
            _youtubeDl = SetupYoutubeDl();

            RegisterEventHandlers();
            RegisterCommands();

            _dj.Start();
            await _client.ConnectAsync();
        }

        DiscordClient SetupDiscordClient()
        {
            return new DiscordClient(File.ReadAllText("bottoken"));
        }

        YoutubeDl SetupYoutubeDl()
        {
            var youtubeOutputFolder = new DirectoryInfo("audio-youtube");
            if (youtubeOutputFolder.Exists == false) youtubeOutputFolder.Create();

            var youtubeTempFolder = new DirectoryInfo("temp-youtube");
            if (youtubeTempFolder.Exists) youtubeTempFolder.Delete(true);
            youtubeTempFolder.Create();

            return new YoutubeDl(youtubeOutputFolder, youtubeTempFolder);
        }

        void RegisterEventHandlers()
        {
            _client.TextChannelMessageReceived += async (message) =>
            {
                try
                {
                    if (message.Author.User.Id == _client.Me.Id) return;
                    await _commandManager.ProcessTextMessageAsync(message);
                }
                catch (CommandException ce)
                {
                    await message.ReplyAsync(ce.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Exception thrown while handling event " + nameof(_client.TextChannelMessageReceived));
                    _logger.LogError(ex);
                }
            };
            _client.ServerCreated += async (server) => {
                try
                {
                    await server.TextChannels.First().SendMessageAsync("bundtbot online");
                    if (server.VoiceChannels.Count() == 0) return;
                    var voiceChannel = server.VoiceChannels.First();
                    _dj.EnqueueAudio(new FileInfo("audio/bbhw.wav"), voiceChannel);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Exception thrown while handling event " + nameof(_client.ServerCreated));
                    _logger.LogError(ex);
                }
			};
            _client.Ready += async (ready) => {
                try
                {
                    _logger.LogInfo("Client is Ready/Connected! ໒( ͡ᵔ ▾ ͡ᵔ )७", ConsoleColor.Green);
                    _logger.LogInfo("Setting game...");
                    await _client.SetGameAsync(Assembly.GetEntryAssembly().GetName().Version.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex);
                }
			};
			_client.TextChannelCreated += async (textChannel) => {
				try {
					await textChannel.SendMessageAsync("less is more");
				} catch (Exception ex) {
					_logger.LogError(ex);
				}
			};
        }

        void RegisterCommands()
        {
            _commandManager.CommandPrefix = "!";

            _commandManager.AddCommand(new TextCommand("hi", async (message, receivedCommand) =>
            {
                await message.ReplyAsync("hi...");
            }));
            _commandManager.AddCommand(new TextCommand("help", async (message, receivedCommand) =>
            {
                var helpMessage = "";
                _commandManager.GetCommands().ToList().ForEach(x => helpMessage += $"`{x.Name}` ");
                await message.ReplyAsync("help me help you: " + helpMessage);
            }));
            _commandManager.AddCommand(new TextCommand("next", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.Next();
                    await message.ReplyAsync("Yea, I wasn't a huge fan of that song either :track_next:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("stop", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.StopAudioAsync();
                    await message.ReplyAsync("Please don't :stop_button: the music :frowning:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("resume", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.ResumeAudio();
                    await message.ReplyAsync("Green light! :arrow_forward:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("pause", async (message, receivedCommand) =>
            {
                try
                {
                    await _dj.PauseAudioAsync();
                    await message.ReplyAsync("Red Light! :rotating_light:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("faster", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.FastForward();
                    await message.ReplyAsync("Double time!");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("slower", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.SloMo();
                    await message.ReplyAsync("Half time!");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("nofx", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.StopEffects();
                    await message.ReplyAsync("Single time!...?");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
            }));
            _commandManager.AddCommand(new TextCommand("echo", async (message, receivedCommand) =>
            {
                await message.ReplyAsync(receivedCommand.ArgumentsString);
            }, minimumArgCount: 1));
            _commandManager.AddCommand(new TextCommand("yt", async (message, receivedCommand) =>
            {
                // TODO Ensure requesting user is in audio channel
                try
                {
                    YoutubeDlUrl youtubeDlUrl;

                    if (Uri.IsWellFormedUriString(receivedCommand.ArgumentsString, UriKind.Absolute))
                    {
                        youtubeDlUrl = YoutubeDlUrl.FromUrl(new Uri(receivedCommand.ArgumentsString));
                    }
                    else
                    {
                        youtubeDlUrl = YoutubeDlUrl.FromSearchString(receivedCommand.ArgumentsString);
                    }

                    var youtubeInfo = await _youtubeDl.DownloadInfoAsync(youtubeDlUrl);

                    var audioFile = new FileInfo(_youtubeDl.OutputFolder.FullName + '/' + youtubeInfo.Id + ".wav");
                    
                    if (audioFile.Exists)
                    {
                        _dj.EnqueueAudio(audioFile, message.Server.VoiceChannels.First());
                        await message.ReplyAsync($"'{youtubeInfo.Title}' added to queue from cache");
                    }
                    else
                    {
                        var youtubeResult = await _youtubeDl.DownloadAudioAsync(youtubeDlUrl, YoutubeDlAudioFormat.wav, 100);
                        _dj.EnqueueAudio(youtubeResult.DownloadedFile, message.Server.VoiceChannels.First());
                        await message.ReplyAsync($"'{youtubeResult.Info.Title}' added to queue");
                    }
                }
                catch (YoutubeException ye)
                {
                    _logger.LogWarning(ye);
                    await message.ReplyAsync(ye.Message);
                }
            }, minimumArgCount: 1));
        }
    }
}
