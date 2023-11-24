﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Runtime.Serialization;
using System.Text;

using Microsoft.Testing.Framework;
using Microsoft.Testing.Platform.Configurations;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Helpers;
using Microsoft.Testing.Platform.Logging;
using Microsoft.Testing.Platform.Services;

using Moq;

namespace Microsoft.Testing.Platform.UnitTests;

[TestGroup]
public class ConfigurationManagerTests : TestBase
{
    private readonly ServiceProvider _serviceProvider;

    public ConfigurationManagerTests(ITestExecutionContext testExecutionContext)
        : base(testExecutionContext)
    {
        _serviceProvider = new();
        _serviceProvider.AddService(new SystemFileSystem());
    }

    [ArgumentsProvider(nameof(GetConfigurationValueFromJsonData))]
    public async ValueTask GetConfigurationValueFromJson(string jsonFileConfig, string key, string? result)
    {
        Mock<IFileSystem> fileSystem = new();
        fileSystem.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        fileSystem.Setup(x => x.NewFileStream(It.IsAny<string>(), FileMode.Open, FileAccess.Read))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(jsonFileConfig)));
        ConfigurationManager configurationManager = new();
        configurationManager.AddConfigurationSource(() =>
            new JsonConfigurationSource(
                new SystemRuntime(new SystemRuntimeFeature(), new SystemEnvironment(), new SystemProcessHandler()),
                fileSystem.Object, null));
        IConfiguration configuration = await configurationManager.BuildAsync(fileSystem.Object, null);
        Assert.AreEqual(result, configuration[key], $"Expected '{result}' found '{configuration[key]}'");
    }

    internal static IEnumerable<(string JsonFileConfig, string Key, string? Result)> GetConfigurationValueFromJsonData()
    {
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true}}}}", "TestingPlatform:Troubleshooting:CrashDump:Enable", "True");
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true}}}}", "TestingPlatform:Troubleshooting:CrashDump:enable", "True");
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true}}}}", "TestingPlatform:Troubleshooting:CrashDump:Missing", null);
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true}}}}", "TestingPlatform:Troubleshooting:CrashDump", "{\"Enable\": true}");
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true} , \"CrashDump2\": {\"Enable\": true}}}}", "TestingPlatform:Troubleshooting:CrashDump", "{\"Enable\": true}");
        yield return ("{\"TestingPlatform\": {\"Troubleshooting\": {\"CrashDump\": {\"Enable\": true}}}}", "TestingPlatform:", null);
        yield return ("{}", "TestingPlatform:Troubleshooting:CrashDump:Enable", null);
        yield return ("{\"TestingPlatform\": [1,2] }", "TestingPlatform:0", "1");
        yield return ("{\"TestingPlatform\": [1,2] }", "TestingPlatform:1", "2");
        yield return ("{\"TestingPlatform\": [1,2] }", "TestingPlatform", "[1,2]");
        yield return ("{\"TestingPlatform\": { \"Array\" : [ {\"Key\" : \"Value\"} , {\"Key\" : 3} ] } }", "TestingPlatform:Array:0", null);
        yield return ("{\"TestingPlatform\": { \"Array\" : [ {\"Key\" : \"Value\"} , {\"Key\" : 3} ] } }", "TestingPlatform:Array:0:Key", "Value");
        yield return ("{\"TestingPlatform\": { \"Array\" : [ {\"Key\" : \"Value\"} , {\"Key\" : 3} ] } }", "TestingPlatform:Array:1:Key", "3");
    }

    public async ValueTask InvalidJson_Fail()
    {
        Mock<IFileSystem> fileSystem = new();
        fileSystem.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        fileSystem.Setup(x => x.NewFileStream(It.IsAny<string>(), FileMode.Open)).Returns(new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)));
        ConfigurationManager configurationManager = new();
        configurationManager.AddConfigurationSource(() =>
            new JsonConfigurationSource(
                new SystemRuntime(new SystemRuntimeFeature(), new SystemEnvironment(), new SystemProcessHandler()),
                fileSystem.Object, null));
        await Assert.ThrowsAsync<Exception>(() => configurationManager.BuildAsync(fileSystem.Object, null));
    }

    [ArgumentsProvider(nameof(GetConfigurationValueFromJsonData))]
    public async ValueTask GetConfigurationValueFromJsonWithFileLoggerProvider(string jsonFileConfig, string key, string? result)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(jsonFileConfig);

        Mock<IFileSystem> fileSystem = new();
        fileSystem.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        fileSystem.Setup(x => x.NewFileStream(It.IsAny<string>(), FileMode.Open))
            .Returns(new MemoryStream(bytes));
        fileSystem.Setup(x => x.NewFileStream(It.IsAny<string>(), FileMode.Open, FileAccess.Read))
            .Returns(new MemoryStream(bytes));

        Mock<ILogger> loggerMock = new();
        loggerMock.Setup(x => x.IsEnabled(LogLevel.Trace)).Returns(true);

        Mock<IFileLoggerProvider> loggerProviderMock = new();
        loggerProviderMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);

        ConfigurationManager configurationManager = new();
        configurationManager.AddConfigurationSource(() =>
            new JsonConfigurationSource(
                new SystemRuntime(new SystemRuntimeFeature(), new SystemEnvironment(), new SystemProcessHandler()),
                fileSystem.Object, null));

        IConfiguration configuration = await configurationManager.BuildAsync(
            fileSystem.Object,
            loggerProviderMock.Object);
        Assert.AreEqual(result, configuration[key], $"Expected '{result}' found '{configuration[key]}'");

        loggerMock.Verify(x => x.LogAsync(LogLevel.Trace, It.IsAny<string>(), null, LoggingExtensions.Formatter), Times.Once);
    }

    public async ValueTask BuildAsync_EmptyConfigurationSources_ThrowsException()
    {
        ConfigurationManager configurationManager = new();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => configurationManager.BuildAsync(
                _serviceProvider,
                null));
    }

    public async ValueTask BuildAsync_ConfigurationSourcesNotEnabledAsync_ThrowsException()
    {
        Mock<IConfigurationSource> mockConfigurationSource = new();
        mockConfigurationSource.Setup(x => x.IsEnabledAsync()).ReturnsAsync(false);

        ConfigurationManager configurationManager = new();
        configurationManager.AddConfigurationSource(() => mockConfigurationSource.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => configurationManager.BuildAsync(
                _serviceProvider,
                null));

        mockConfigurationSource.Verify(x => x.IsEnabledAsync(), Times.Once);
    }

    public async ValueTask BuildAsync_ConfigurationSourceIsAsyncInitializableExtension_InitializeAsyncIsCalled()
    {
        Mock<IConfigurationProvider> mockConfigurationProvider = new();
        mockConfigurationProvider.Setup(x => x.LoadAsync()).Callback(() => { });

        Mock<FakeConfigurationSource> fakeConfigurationSource = new();
        fakeConfigurationSource.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        fakeConfigurationSource.Setup(x => x.InitializeAsync()).Callback(() => { });
        fakeConfigurationSource.Setup(x => x.Build()).Returns(mockConfigurationProvider.Object);

        ConfigurationManager configurationManager = new();
        configurationManager.AddConfigurationSource(() => fakeConfigurationSource.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => configurationManager.BuildAsync(
                _serviceProvider,
                null));

        fakeConfigurationSource.Verify(x => x.IsEnabledAsync(), Times.Once);
        fakeConfigurationSource.Verify(x => x.InitializeAsync(), Times.Once);
    }
}

internal class FakeConfigurationSource : IConfigurationSource, IAsyncInitializableExtension
{
    public string Uid => nameof(FakeConfigurationSource);

    public string Version => "1.0.0";

    public string DisplayName => nameof(FakeConfigurationSource);

    public string Description => nameof(FakeConfigurationSource);

    public virtual IConfigurationProvider Build() => throw new NotImplementedException();

    public virtual Task InitializeAsync() => throw new NotImplementedException();

    public virtual Task<bool> IsEnabledAsync() => throw new NotImplementedException();
}