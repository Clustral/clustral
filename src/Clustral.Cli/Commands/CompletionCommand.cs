using System.CommandLine;
using System.CommandLine.Invocation;
using Clustral.Cli.Ui;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral completion bash|zsh|fish</c>: generates shell
/// completion scripts that the user pipes to their shell config.
///
/// The scripts are static — they register the command tree (subcommands,
/// options, arguments) with the shell's completion system. No runtime
/// <c>dotnet-suggest</c> dependency.
/// </summary>
internal static class CompletionCommand
{
    private static readonly Argument<string> ShellArg = new(
        "shell",
        "Shell type: bash, zsh, or fish.");

    public static Command Build()
    {
        var cmd = new Command("completion", "Generate shell completion scripts.");
        cmd.AddArgument(ShellArg);
        cmd.SetHandler(Handle);
        return cmd;
    }

    private static void Handle(InvocationContext ctx)
    {
        var shell = ctx.ParseResult.GetValueForArgument(ShellArg);

        var script = shell.ToLowerInvariant() switch
        {
            "bash" => BashScript,
            "zsh"  => ZshScript,
            "fish" => FishScript,
            _ => null,
        };

        if (script is null)
        {
            CliErrors.WriteError($"Unknown shell: '{shell}'. Supported: bash, zsh, fish.");
            ctx.ExitCode = 1;
            return;
        }

        Console.Write(script);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Static completion scripts
    // ─────────────────────────────────────────────────────────────────────────

    internal const string BashScript = """
        # clustral bash completion — add to ~/.bashrc:
        #   eval "$(clustral completion bash)"

        _clustral_completions() {
            local cur prev commands
            cur="${COMP_WORDS[COMP_CWORD]}"
            prev="${COMP_WORDS[COMP_CWORD-1]}"

            commands="login logout kube clusters users roles access config status doctor profile whoami update version completion"

            case "${prev}" in
                clustral)
                    COMPREPLY=($(compgen -W "${commands}" -- "${cur}"))
                    return 0
                    ;;
                kube)
                    COMPREPLY=($(compgen -W "login logout list ls" -- "${cur}"))
                    return 0
                    ;;
                clusters)
                    COMPREPLY=($(compgen -W "list ls" -- "${cur}"))
                    return 0
                    ;;
                users)
                    COMPREPLY=($(compgen -W "list ls" -- "${cur}"))
                    return 0
                    ;;
                roles)
                    COMPREPLY=($(compgen -W "list ls" -- "${cur}"))
                    return 0
                    ;;
                access)
                    COMPREPLY=($(compgen -W "request list ls approve deny revoke" -- "${cur}"))
                    return 0
                    ;;
                config)
                    COMPREPLY=($(compgen -W "show path clean" -- "${cur}"))
                    return 0
                    ;;
                profile)
                    COMPREPLY=($(compgen -W "create use list ls current delete" -- "${cur}"))
                    return 0
                    ;;
                completion)
                    COMPREPLY=($(compgen -W "bash zsh fish" -- "${cur}"))
                    return 0
                    ;;
                --output|-o)
                    COMPREPLY=($(compgen -W "table json" -- "${cur}"))
                    return 0
                    ;;
            esac

            if [[ "${cur}" == -* ]]; then
                COMPREPLY=($(compgen -W "--debug --output --no-color --insecure --help" -- "${cur}"))
                return 0
            fi
        }

        complete -F _clustral_completions clustral
        """;

    internal const string ZshScript = """
        # clustral zsh completion — add to ~/.zshrc:
        #   eval "$(clustral completion zsh)"

        _clustral() {
            local -a commands subcommands options

            commands=(
                'login:Sign in via OIDC'
                'logout:Sign out and revoke credentials'
                'kube:Manage kubeconfig credentials'
                'clusters:Manage registered clusters'
                'users:Manage users'
                'roles:Manage roles'
                'access:JIT access requests'
                'config:Show CLI configuration'
                'status:Show session and cluster status'
                'doctor:Diagnose connectivity issues'
                'update:Update to latest version'
                'version:Show CLI and ControlPlane versions'
                'profile:Manage configuration profiles'
                'whoami:Show current user and session'
                'completion:Generate shell completions'
            )

            if (( CURRENT == 2 )); then
                _describe 'command' commands
                return
            fi

            case "${words[2]}" in
                kube)
                    subcommands=('login:Issue kubeconfig credential' 'logout:Revoke credential' 'list:List clusters' 'ls:List clusters')
                    _describe 'subcommand' subcommands
                    ;;
                clusters|users|roles)
                    subcommands=('list:List all' 'ls:List all')
                    _describe 'subcommand' subcommands
                    ;;
                access)
                    subcommands=('request:Request access' 'list:List requests' 'ls:List requests' 'approve:Approve request' 'deny:Deny request' 'revoke:Revoke grant')
                    _describe 'subcommand' subcommands
                    ;;
                profile)
                    subcommands=('create:Create profile' 'use:Switch profile' 'list:List profiles' 'ls:List profiles' 'current:Show active' 'delete:Delete profile')
                    _describe 'subcommand' subcommands
                    ;;
                completion)
                    subcommands=('bash:Bash completion' 'zsh:Zsh completion' 'fish:Fish completion')
                    _describe 'shell' subcommands
                    ;;
            esac
        }

        compdef _clustral clustral
        """;

    internal const string FishScript = """
        # clustral fish completion — save to:
        #   clustral completion fish > ~/.config/fish/completions/clustral.fish

        # Top-level commands
        complete -c clustral -n '__fish_use_subcommand' -a login -d 'Sign in via OIDC'
        complete -c clustral -n '__fish_use_subcommand' -a logout -d 'Sign out and revoke credentials'
        complete -c clustral -n '__fish_use_subcommand' -a kube -d 'Manage kubeconfig credentials'
        complete -c clustral -n '__fish_use_subcommand' -a clusters -d 'Manage registered clusters'
        complete -c clustral -n '__fish_use_subcommand' -a users -d 'Manage users'
        complete -c clustral -n '__fish_use_subcommand' -a roles -d 'Manage roles'
        complete -c clustral -n '__fish_use_subcommand' -a access -d 'JIT access requests'
        complete -c clustral -n '__fish_use_subcommand' -a config -d 'Show CLI configuration'
        complete -c clustral -n '__fish_use_subcommand' -a status -d 'Show session and cluster status'
        complete -c clustral -n '__fish_use_subcommand' -a doctor -d 'Diagnose connectivity issues'
        complete -c clustral -n '__fish_use_subcommand' -a update -d 'Update to latest version'
        complete -c clustral -n '__fish_use_subcommand' -a version -d 'Show versions'
        complete -c clustral -n '__fish_use_subcommand' -a profile -d 'Manage configuration profiles'
        complete -c clustral -n '__fish_use_subcommand' -a whoami -d 'Show current user and session'
        complete -c clustral -n '__fish_use_subcommand' -a completion -d 'Generate shell completions'

        # kube subcommands
        complete -c clustral -n '__fish_seen_subcommand_from kube' -a login -d 'Issue kubeconfig credential'
        complete -c clustral -n '__fish_seen_subcommand_from kube' -a logout -d 'Revoke credential'
        complete -c clustral -n '__fish_seen_subcommand_from kube' -a list -d 'List clusters'
        complete -c clustral -n '__fish_seen_subcommand_from kube' -a ls -d 'List clusters'

        # clusters/users/roles subcommands
        complete -c clustral -n '__fish_seen_subcommand_from clusters' -a 'list ls' -d 'List all'
        complete -c clustral -n '__fish_seen_subcommand_from users' -a 'list ls' -d 'List all'
        complete -c clustral -n '__fish_seen_subcommand_from roles' -a 'list ls' -d 'List all'

        # access subcommands
        complete -c clustral -n '__fish_seen_subcommand_from access' -a request -d 'Request access'
        complete -c clustral -n '__fish_seen_subcommand_from access' -a 'list ls' -d 'List requests'
        complete -c clustral -n '__fish_seen_subcommand_from access' -a approve -d 'Approve request'
        complete -c clustral -n '__fish_seen_subcommand_from access' -a deny -d 'Deny request'
        complete -c clustral -n '__fish_seen_subcommand_from access' -a revoke -d 'Revoke grant'

        # profile subcommands
        complete -c clustral -n '__fish_seen_subcommand_from profile' -a create -d 'Create profile'
        complete -c clustral -n '__fish_seen_subcommand_from profile' -a use -d 'Switch profile'
        complete -c clustral -n '__fish_seen_subcommand_from profile' -a 'list ls' -d 'List profiles'
        complete -c clustral -n '__fish_seen_subcommand_from profile' -a current -d 'Show active profile'
        complete -c clustral -n '__fish_seen_subcommand_from profile' -a delete -d 'Delete profile'

        # completion subcommands
        complete -c clustral -n '__fish_seen_subcommand_from completion' -a 'bash zsh fish' -d 'Shell type'

        # Global options
        complete -c clustral -l debug -d 'Enable debug output'
        complete -c clustral -l no-color -d 'Disable colored output'
        complete -c clustral -s o -l output -a 'table json' -d 'Output format'
        complete -c clustral -l insecure -d 'Skip TLS verification'
        """;
}
