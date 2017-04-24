﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BundtCord.Discord;
using DiscordApiWrapper.Audio;

namespace BundtBot
{
    public class BundtBot
    {
        static readonly MyLogger _logger = new MyLogger(nameof(BundtBot));

        DiscordClient _client;
        DJ _dj = new DJ();
        CommandManager _commandManager = new CommandManager();

        public async Task StartAsync()
        {
            _client = new DiscordClient(File.ReadAllText("bottoken"));

            RegisterEventHandlers();
            RegisterCommands();
            _dj.Start();

            await _client.ConnectAsync();
        }

        void RegisterEventHandlers()
        {
            _client.TextChannelMessageReceived += async (message) =>
            {
                try
                {
                    if (message.Author.User.Id == _client.Me.Id) return;
                    _commandManager.ProcessTextMessage(message);
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
                    var fullSongPcm = new WavFileReader().ReadFileBytes(new FileInfo("audio/bbhw.wav"));
                    _dj.EnqueueAudio(fullSongPcm, voiceChannel);
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
            
            _commandManager.Commands.Add(new TextCommand("hi", async (message, receivedCommand) =>
            {
                try
                {
                    await message.ReplyAsync("hi...");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex);
                }
            }));
            _commandManager.Commands.Add(new TextCommand("help", async (message, receivedCommand) =>
            {
                try
                {
                    var helpMessage = "";
                    _commandManager.Commands.ForEach(x => helpMessage += $"`{x.Name}` ");
                    await message.ReplyAsync("help me help you: " + helpMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex);
                }
            }));
            _commandManager.Commands.Add(new TextCommand("next", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.Next();
                    await message.ReplyAsync("Yea, I wasn't a huge fan of that song either :track_next:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
                catch (Exception ex) { _logger.LogError(ex); }
            }));
            _commandManager.Commands.Add(new TextCommand("stop", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.StopAudioAsync();
                    await message.ReplyAsync("Please don't :stop_button: the music :frowning:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
                catch (Exception ex) { _logger.LogError(ex); }
            }));
            _commandManager.Commands.Add(new TextCommand("resume", async (message, receivedCommand) =>
            {
                try
                {
                    _dj.ResumeAudio();
                    await message.ReplyAsync("Green light! :arrow_forward:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
                catch (Exception ex) { _logger.LogError(ex); }
            }));
            _commandManager.Commands.Add(new TextCommand("pause", async (message, receivedCommand) =>
            {
                try
                {
                    await _dj.PauseAudioAsync();
                    await message.ReplyAsync("Red Light! :rotating_light:");
                }
                catch (DJException dje) { await message.ReplyAsync(dje.Message); }
                catch (Exception ex) { _logger.LogError(ex); }
            }));
            _commandManager.Commands.Add(new TextCommand("echo", async (message, receivedCommand) =>
            {
                try
                {
                    await message.ReplyAsync(receivedCommand.ArgsString);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex);
                }
            }, minimumArgCount: 1));
            _commandManager.Commands.Add(new TextCommand("yt", async (message, receivedCommand) =>
            {
                try
                {
                    // Check if arg is a url
                    if (Uri.IsWellFormedUriString(receivedCommand.ArgsString, UriKind.Absolute) == false)
                    {
                        await message.ReplyAsync("Only URL's are accepted for thee moment");
                        return;
                    }

                    var uri = new Uri(receivedCommand.ArgsString);

                    // TODO Allow more URLs
                    if (uri.Host != "www.youtube.com")
                    {
                        await message.ReplyAsync("Only youtube URL's are accepted for the moment");
                        return;
                    }

                    // Use youtube-dl to download url as wav

                    var outputfolder = new DirectoryInfo("audio");
                    if (outputfolder.Exists == false) outputfolder.Create();

                    var youtubeOutput = await new YoutubeDownloader().YoutubeDownloadAndConvertAsync(message, uri.ToString(), outputfolder);


                    var fullSongPcm = new WavFileReader().ReadFileBytes(youtubeOutput);
                    _dj.EnqueueAudio(fullSongPcm, message.Server.VoiceChannels.First());
                    await message.ReplyAsync(uri.ToString() + " added to queue");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex);
                }
            }, minimumArgCount: 1));
        }
    }
}
