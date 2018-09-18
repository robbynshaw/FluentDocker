using System;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Model.Compose
{
  public sealed class DockerComposeConfig
  {
    /// <summary>
    ///   Fully qualified path to the docker-compose file.
    /// </summary>
    public string ComposeFilePath { get; set; }
    public bool ForceRecreate { get; set; }
    public bool NoRecreate { get; set; }
    public bool NoBuild { get; set; }
    public bool ForceBuild { get; set; }
    public TimeSpan TimeoutSeconds { get; set; }
    public bool RemoveOrphans { get; set; }
    public string AlternativeServiceName { get; set; }
    public bool UseColor { get; set; }
    public bool KeepVolumes { get; set; }
    public ImageRemovalOption ImageRemoval { get; set; }
    public string []Services { get; set; }
  }
}