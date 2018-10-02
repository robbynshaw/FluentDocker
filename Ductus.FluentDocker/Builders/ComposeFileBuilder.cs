using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  [Experimental(TargetVersion = "3.0.0")]
  public sealed class ComposeFileBuilder : BaseBuilder<ICompositeService>
  {
    private readonly DockerComposeFileConfig _config = new DockerComposeFileConfig();

    internal ComposeFileBuilder(IBuilder parent, string composeFile = null) : base(parent)
    {
      _config.ComposeFilePath = composeFile;
    }

    public override ICompositeService Build()
    {
      if (string.IsNullOrEmpty(_config.ComposeFilePath))
        throw new FluentDockerException("Cannot create service without a docker-compose file");

      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build service using compose-file {_config.ComposeFilePath} since no host service is defined");

      var container = new DockerComposeCompositeService(host.Value, _config);

      AddHooks(container);

      return container;
    }

    private void AddHooks(ICompositeService container)
    {
      IContainerService Resolve(string name)
      {
        return container.Containers.FirstOrDefault(x => x.Name == name);
      }

      foreach (var config in _config.ContainerConfiguration.Values)
      {
        // Copy files just before starting
        if (null != config.CpToOnStart)
          container.AddHook(ServiceRunningState.Starting,
            service =>
            {
              foreach (var copy in config.CpToOnStart)
                Resolve(config.Name)?.CopyTo(copy.Item2, copy.Item1);
            });

        // Wait for port when started
        if (null != config.WaitForPort)
          container.AddHook(ServiceRunningState.Running,
            service => { Resolve(config.Name)?.WaitForPort(config.WaitForPort.Item1, config.WaitForPort.Item2); });

        // Wait for http when started
        if (null != config.WaitForHttp && 0 != config.WaitForHttp.Count)
          container.AddHook(ServiceRunningState.Running, service =>
          {
            foreach (var prm in config.WaitForHttp)
              Resolve(config.Name)?.WaitForHttp(prm.Url, prm.Timeout, prm.Continuation, prm.Method, prm.ContentType,
                prm.Body);
          });

        // Wait for process when started
        if (null != config.WaitForProcess)
          container.AddHook(ServiceRunningState.Running,
            service =>
            {
              Resolve(config.Name)?.WaitForProcess(config.WaitForProcess.Item1, config.WaitForProcess.Item2);
            });

        // docker execute on running
        if (null != config.ExecuteOnRunningArguments && config.ExecuteOnRunningArguments.Count > 0)
          container.AddHook(ServiceRunningState.Running, service =>
          {
            var svc = Resolve(config.Name);
            if (null == svc) return;

            foreach (var binaryAndArguments in config.ExecuteOnRunningArguments)
            {
              var result = svc.DockerHost.Execute(svc.Id, binaryAndArguments, svc.Certificates);
              if (!result.Success)
                throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
            }
          });

        // Copy files / folders on dispose
        if (null != config.CpFromOnDispose && 0 != config.CpFromOnDispose.Count)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            foreach (var copy in config.CpFromOnDispose)
              Resolve(config.Name)?.CopyFrom(copy.Item2, copy.Item1);
          });

        // docker execute when disposing
        if (null != config.ExecuteOnDisposingArguments && config.ExecuteOnDisposingArguments.Count > 0)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            var svc = Resolve(config.Name);
            if (null == svc) return;

            foreach (var binaryAndArguments in config.ExecuteOnDisposingArguments)
            {
              var result = svc.DockerHost.Execute(svc.Id, binaryAndArguments, svc.Certificates);
              if (!result.Success)
                throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
            }
          });

        // Export container on dispose
        if (null != config.ExportOnDispose)
          container.AddHook(ServiceRunningState.Removing, service =>
          {
            var svc = Resolve(config.Name);
            if (null == svc) return;

            if (config.ExportOnDispose.Item3(svc))
              svc.Export(config.ExportOnDispose.Item1, config.ExportOnDispose.Item2);
          });
      }
    }

    public ComposeFileBuilder FromFile(string composeFile)
    {
      _config.ComposeFilePath = composeFile;
      return this;
    }

    public ComposeFileBuilder ForceRecreate()
    {
      _config.ForceRecreate = true;
      return this;
    }

    public ComposeFileBuilder NoRecreate()
    {
      _config.NoRecreate = true;
      return this;
    }

    public ComposeFileBuilder NoBuild()
    {
      _config.NoBuild = true;
      return this;
    }

    public ComposeFileBuilder ForceBuild()
    {
      _config.ForceBuild = true;
      return this;
    }

    public ComposeFileBuilder Timeout(TimeSpan timeoutInSeconds)
    {
      _config.TimeoutSeconds = timeoutInSeconds;
      return this;
    }

    public ComposeFileBuilder RemoveOrphans()
    {
      _config.RemoveOrphans = true;
      return this;
    }

    public ComposeFileBuilder ServiceName(string name)
    {
      _config.AlternativeServiceName = name;
      return this;
    }

    public ComposeFileBuilder UseColor()
    {
      _config.UseColor = true;
      return this;
    }

    public ComposeFileBuilder KeepVolumes()
    {
      _config.KeepVolumes = true;
      return this;
    }

    public ComposeFileBuilder RemoveAllImages()
    {
      _config.ImageRemoval = ImageRemovalOption.All;
      return this;
    }

    public ComposeFileBuilder RemoveNonTaggedImages()
    {
      _config.ImageRemoval = ImageRemovalOption.Local;
      return this;
    }

    public ComposeFileBuilder KeepOnDispose()
    {
      _config.StopOnDispose = false;
      return this;
    }

    public ComposeFileBuilder ExportOnDispose(string service, string hostPath,
      Func<IContainerService, bool> condition = null)
    {
      GetContainerSpecificConfig(service).ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), false /*no-explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ComposeFileBuilder ExportExploadedOnDispose(string service, string hostPath,
      Func<IContainerService, bool> condition = null)
    {
      GetContainerSpecificConfig(service).ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), true /*explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ComposeFileBuilder CopyOnStart(string service, string hostPath, string containerPath)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.CpToOnStart)
        config.CpToOnStart = new List<Tuple<TemplateString, TemplateString>>();

      config.CpToOnStart.Add(
        new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(), containerPath.EscapePath()));
      return this;
    }

    public ComposeFileBuilder CopyOnDispose(string service, string containerPath, string hostPath)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.CpFromOnDispose)
        config.CpFromOnDispose = new List<Tuple<TemplateString, TemplateString>>();

      config.CpFromOnDispose.Add(
        new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(), containerPath.EscapePath()));
      return this;
    }

    public ComposeFileBuilder WaitForPort(string service, string portAndProto, long millisTimeout = long.MaxValue)
    {
      GetContainerSpecificConfig(service).WaitForPort = new Tuple<string, long>(portAndProto, millisTimeout);
      return this;
    }

    public ComposeFileBuilder WaitForProcess(string service, string process, long millisTimeout = long.MaxValue)
    {
      GetContainerSpecificConfig(service).WaitForProcess = new Tuple<string, long>(process, millisTimeout);
      return this;
    }

    /// <summary>
    ///   Executes one or more commands including their arguments when container has started.
    /// </summary>
    /// <param name="service">The service to execute on</param>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public ComposeFileBuilder ExecuteOnRunning(string service, params string[] execute)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.ExecuteOnRunningArguments) config.ExecuteOnRunningArguments = new List<string>();

      config.ExecuteOnRunningArguments.AddRange(execute);
      return this;
    }

    /// <summary>
    ///   Executes one or more commands including their arguments when container about to stop.
    /// </summary>
    /// <param name="service">The service to execute on</param>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public ComposeFileBuilder ExecuteOnDisposing(string service, params string[] execute)
    {
      var config = GetContainerSpecificConfig(service);
      if (null == config.ExecuteOnDisposingArguments) config.ExecuteOnDisposingArguments = new List<string>();

      config.ExecuteOnDisposingArguments.AddRange(execute);
      return this;
    }

    /// <summary>
    ///   Waits for a request to be passed or failed.
    /// </summary>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="continuation">Optional continuation that evaluates if it shall still wait or continue.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    public ComposeFileBuilder WaitForHttp(string service, string url, long timeout = 60_000,
      Func<RequestResponse, int, long> continuation = null, HttpMethod method = null,
      string contentType = "application/json", string body = null)
    {
      var config = GetContainerSpecificConfig(service);
      config.WaitForHttp.Add(new ContainerSpecificConfig.WaitForHttpParams
      {
        Url = url, Timeout = timeout, Continuation = continuation, Method = method, ContentType = contentType,
        Body = body
      });

      return this;
    }

    private ContainerSpecificConfig GetContainerSpecificConfig(string service)
    {
      if (_config.ContainerConfiguration.TryGetValue(service, out var config)) return config;

      config = new ContainerSpecificConfig {Name = service};
      _config.ContainerConfiguration.Add(service, config);

      return config;
    }

    protected override IBuilder InternalCreate()
    {
      return new ComposeFileBuilder(this);
    }
  }
}