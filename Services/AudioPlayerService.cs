﻿using Disqord;
using Disqord.Bot.Hosting;
using Disqord.Extensions.Voice;
using Disqord.Gateway;
using Disqord.Voice;
using HidamariBot.Audio;
using Microsoft.Extensions.Logging;
using Qmmands;

namespace HidamariBot.Services;

public class AudioPlayerService : DiscordBotService {
    readonly SemaphoreSlim _semaphore = new(1, 1);
    HttpClient? _httpClient;
    AudioPlayer? _audioPlayer;

    const string RADIO_URL = "https://stream.r-a-d.io/main.mp3";

    public async Task<IResult> PlayRadio(Snowflake guildId, Snowflake channelId) {
        try {
            VoiceExtension voiceExtension = Bot.GetRequiredExtension<VoiceExtension>();

            await _semaphore.WaitAsync();

            IVoiceConnection voiceConnection = await voiceExtension.ConnectAsync(guildId, channelId);

            _httpClient = new HttpClient();
            Stream stream = await _httpClient.GetStreamAsync(RADIO_URL);
            var audioSource = new FFmpegAudioSource(stream);
            _audioPlayer = new AudioPlayer(voiceConnection);

            if (_audioPlayer.TrySetSource(audioSource)) {
                _audioPlayer.Start();
                return Results.Success;
            }

            return Results.Failure("Impossible de diffuser la radio.");
        } catch (Exception ex) {
            Logger.LogError(ex, "Error while trying to start the radio");
            return Results.Failure("Une erreur est survenue !");
        } finally {
            _semaphore.Release();
        }
    }

    public async Task<IResult> StopRadio(Snowflake guildId) {
        VoiceExtension voiceExtension = Bot.GetRequiredExtension<VoiceExtension>();

        await _semaphore.WaitAsync();

        try {
            if (_audioPlayer != null) {
                _audioPlayer.Stop();
                await _audioPlayer.DisposeAsync();
                _audioPlayer = null;
            }

            if (_httpClient != null) {
                _httpClient.Dispose();
                _httpClient = null;
            }

            await voiceExtension.DisconnectAsync(guildId);
        } catch (Exception ex) {
            Logger.LogError(ex, "Error while trying to stop the radio");
            return Results.Failure("Une erreur est survenue lors de la déconnexion");
        } finally {
            _semaphore.Release();
        }

        return Results.Success;
    }

    public CachedVoiceState? GetBotVoiceState(Snowflake guildId) {
        return Bot.GetVoiceState(guildId, Bot.CurrentUser.Id);
    }

    public CachedVoiceState? GetMemberVoiceState(Snowflake guildId, Snowflake memberId) {
        return Bot.GetVoiceState(guildId, memberId);
    }

    bool IsVoiceChannelEmpty(Snowflake guildId, Snowflake channelId) {
        IReadOnlyDictionary<Snowflake, CachedVoiceState> voiceStates = Bot.GetVoiceStates(guildId);
        return !voiceStates.Any(vs => vs.Value.ChannelId == channelId && vs.Key != Bot.CurrentUser.Id);
    }

    protected override ValueTask OnReady(ReadyEventArgs e) {
        Logger.LogInformation("AudioPlayerService Ready fired!");
        return default;
    }

    protected override async ValueTask OnVoiceStateUpdated(VoiceStateUpdatedEventArgs e) {
        if (e.MemberId == Bot.CurrentUser.Id && e.NewVoiceState.ChannelId == null) {
            await StopRadio(e.GuildId);
        }

        CachedVoiceState? botVoiceState = GetBotVoiceState(e.GuildId);
        if (botVoiceState != null && botVoiceState.ChannelId.HasValue) {
            if (IsVoiceChannelEmpty(e.GuildId, botVoiceState.ChannelId.Value)) {
                Logger.LogInformation("Bot left voice channel in guild {GuildId} because it became empty", e.GuildId);
                await StopRadio(e.GuildId);
            }
        }
    }
}
