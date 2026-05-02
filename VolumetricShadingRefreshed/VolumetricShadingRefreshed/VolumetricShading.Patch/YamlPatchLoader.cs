using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VolumetricShadingRefreshed.VolumetricShading.Patch;

public class YamlPatchLoader
{
    public static readonly AssetCategory ShaderPatches = new("shaderpatches", false, (EnumAppSide)2);

    public static readonly AssetCategory ShaderSnippets = new("shadersnippets", false, (EnumAppSide)2);

    private readonly ICoreClientAPI _capi;

    private readonly string _domain;

    private readonly ShaderPatcher _patcher;

    public YamlPatchLoader(ShaderPatcher patcher, string domain, ICoreClientAPI capi)
    {
        _patcher = patcher;
        _domain = domain;
        _capi = capi;
    }

    public void Load()
    {
        _capi.Assets.Reload(ShaderPatches);
        _capi.Assets.Reload(ShaderSnippets);
        foreach (var item in _capi.Assets.GetMany("shaderpatches", _domain))
        {
            LoadFromYaml(item.ToText());
        }
    }

    public void LoadFromYaml(string yaml)
    {
        foreach (var item in new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                     .Build().Deserialize<IList<PatchEntry>>(yaml))
        {
            var content = item.Content;
            if (!string.IsNullOrEmpty(item.Snippet))
            {
                content = _capi.Assets.Get(new AssetLocation(_domain, "shadersnippets/" + item.Snippet))
                    .ToText();
            }

            switch (item.Type)
            {
                case "start":
                    AddStartPatch(item, content);
                    break;
                case "end":
                    AddEndPatch(item, content);
                    break;
                case "regex":
                    AddRegexPatch(item, content);
                    break;
                case "token":
                    AddTokenPatch(item, content);
                    break;
                default:
                    throw new ArgumentException("Invalid type " + item.Type);
            }
        }
    }

    private void AddTokenPatch(PatchEntry patch, string content)
    {
        var tokenPatch = patch.Filename == null
            ? new TokenPatch(patch.Tokens)
            : new TokenPatch(patch.Filename, patch.Tokens);
        tokenPatch.Optional = patch.Optional;
        tokenPatch.Multiple = patch.Multiple;
        tokenPatch.ReplacementString = content;
        _patcher.AddPatch(tokenPatch);
    }

    private void AddRegexPatch(PatchEntry patch, string content)
    {
        var regexPatch = patch.Filename == null
            ? new RegexPatch(patch.Regex)
            : new RegexPatch(patch.Filename, patch.Regex);
        regexPatch.Multiple = patch.Multiple;
        regexPatch.Optional = patch.Optional;
        regexPatch.ReplacementString = content;
        _patcher.AddPatch(regexPatch);
    }

    private void AddEndPatch(PatchEntry patch, string content)
    {
        if (patch.Filename == null)
        {
            _patcher.AddAtEnd(content);
        }
        else
        {
            _patcher.AddAtEnd(patch.Filename, content);
        }
    }

    private void AddStartPatch(PatchEntry patch, string content)
    {
        if (patch.Filename == null)
        {
            _patcher.AddAtStart(content);
        }
        else
        {
            _patcher.AddAtStart(patch.Filename, content);
        }
    }

    private class PatchEntry
    {
        public string Content;

        public string Filename;

        public bool Multiple;

        public bool Optional;

        public string Regex;

        public string Snippet;

        public string Tokens;
        public string Type;
    }
}
