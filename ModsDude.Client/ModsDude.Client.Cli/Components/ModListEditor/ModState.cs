using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal abstract class ModState(Mod mod)
{
    public Mod Mod { get; } = mod;


    public abstract ModState Reset();


    public class Included(Mod.Version version) : ModState(version.Parent)
    {
        public bool UpdateAvailable { get; } = version.Parent.Latest != version;
        public Mod.Version Version { get; } = version;


        public Removed Remove()
        {
            return new Removed(Version);
        }

        public ModState Update()
        {
            if (!UpdateAvailable)
            {
                return this;
            }

            return new ChangedVersion(Version, Version.Parent.Latest);
        }

        public ModState ChangeVersion(Mod.Version to)
        {
            if (to == Version)
            {
                throw new InvalidOperationException("Cannot change to same version.");
            }

            EnsureVersionIsOfThisMod(to);

            return new ChangedVersion(Version, to);
        }

        public override ModState Reset()
        {
            return this;
        }
    }


    public class Available : ModState
    {
        public Available(Mod mod)
            : base(mod)
        {
        }


        public Added Add()
        {
            return new Added(Mod.Latest);
        }

        public override ModState Reset()
        {
            return this;
        }
    }


    public class Added : ModState
    {
        public Added(Mod.Version version)
            : base(version.Parent)
        {
            Version = version;
        }


        public Mod.Version Version { get; }


        public Available Remove()
        {
            return new Available(Mod);
        }

        public Added ChangeVersion(Mod.Version to)
        {
            EnsureVersionIsOfThisMod(to);

            return new Added(to);
        }

        public override ModState Reset()
        {
            return new Available(Mod);
        }
    }


    public class ChangedVersion : ModState
    {
        public ChangedVersion(Mod.Version from, Mod.Version to)
            : base(from.Parent)
        {
            EnsureVersionIsOfThisMod(to);
            From = from;
            To = to;
        }


        public Mod.Version From { get; }
        public Mod.Version To { get; }


        public ChangedVersion ChangeVersion(Mod.Version to)
        {
            EnsureVersionIsOfThisMod(to);

            return new ChangedVersion(From, to);
        }

        public ModState Remove()
        {
            return new Removed(From);
        }

        public override ModState Reset()
        {
            return new Included(From);
        }
    }


    public class Removed : ModState
    {
        public Removed(Mod.Version version)
            : base(version.Parent)
        {
            Version = version;
        }


        public Mod.Version Version { get; }


        public Included Add()
        {
            return new Included(Version);
        }

        public override ModState Reset()
        {
            return new Included(Version);
        }
    }


    protected void EnsureVersionIsOfThisMod(Mod.Version version)
    {
        if (!Mod.Versions.Contains(version))
        {
            throw new ArgumentException($"Supplied version is not of this mod.");
        }
    }
}
