using Nmw.Core.Workspace;

namespace Nmw.App.Models;

/// <summary>연결 파라미터 ↔ 워크스페이스 채널 설정 매핑.</summary>
public static class WorkspaceMapping
{
    /// <summary>연결 파라미터를 채널 설정으로 변환한다.</summary>
    /// <param name="parameters">연결 파라미터.</param>
    /// <param name="channelId">채널 ID.</param>
    public static ChannelConfig ToChannelConfig(ConnectionParameters parameters, string channelId) => new()
    {
        Id = channelId,
        Type = parameters.Mode switch
        {
            ConnectionMode.Rtu => ChannelType.Rtu,
            ConnectionMode.RtuOverTcp => ChannelType.RtuOverTcp,
            _ => ChannelType.Tcp,
        },
        Host = parameters.Host,
        Port = parameters.Port,
        PortName = parameters.SerialPortName,
        BaudRate = parameters.BaudRate,
        Parity = parameters.Parity,
        DataBits = parameters.DataBits,
        StopBits = parameters.StopBits,
        TimeoutMs = parameters.TimeoutMs,
        Retries = parameters.Retries,
        InterFrameDelayMs = parameters.InterFrameDelayMs,
    };

    /// <summary>채널 설정을 연결 파라미터로 변환한다.</summary>
    /// <param name="config">채널 설정.</param>
    public static ConnectionParameters ToParameters(ChannelConfig config) => new()
    {
        Mode = config.Type switch
        {
            ChannelType.Rtu => ConnectionMode.Rtu,
            ChannelType.RtuOverTcp => ConnectionMode.RtuOverTcp,
            _ => ConnectionMode.Tcp,
        },
        Host = config.Host,
        Port = config.Port,
        SerialPortName = config.PortName,
        BaudRate = config.BaudRate,
        Parity = config.Parity,
        DataBits = config.DataBits,
        StopBits = config.StopBits,
        TimeoutMs = config.TimeoutMs,
        Retries = config.Retries,
        InterFrameDelayMs = config.InterFrameDelayMs,
    };
}
