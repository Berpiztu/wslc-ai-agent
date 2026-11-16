using System.Text;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The shell script Compact VHDX runs inside the temporary session it opens on
/// a stopped store, where the store's disk is mounted and nothing uses it.
/// <c>wslc</c> has no command that drops the build cache, nor one for layers
/// left behind, and a VHDX only shrinks by what is free inside it; so the
/// script reads the store's own records and says, and on <c>clean</c> removes,
/// what no image and no container stands on:
/// <list type="bullet">
/// <item>layers no image is made of and no container is created from. A
/// container's writable layer that outlived its container (a removal cut
/// short) holds every layer under it for good, so only the writable layers of
/// containers still there count;</item>
/// <item>those outlived writable layers themselves;</item>
/// <item>the build cache, whole: its records and its folders.</item>
/// </list>
/// Images, containers and volumes are never touched. It stops, removing
/// nothing, on a store not laid out the way it knows. Every line it means to
/// be read starts with <c>::</c>, which tells them from the shell's echo of
/// what was typed; a <c>STEP</c> line says what it is doing, since reading a
/// large store takes a while, and a <c>TICK</c> line how far it has got, one
/// after another meant to be written over the last: a screen that stands
/// still for a minute looks like one that hung, and gets closed.
/// </summary>
public static class StoreCleanupScript
{
    /// <summary>Where the script is written inside the temporary session: its root is memory, gone with it.</summary>
    public const string GuestPath = "/tmp/wslc-agent-store.sh";

    /// <summary>What the store holds and what could go; nothing is changed.</summary>
    public const string Report = "report";

    /// <summary>Removes what <see cref="Report"/> says can go, then trims.</summary>
    public const string Clean = "clean";

    /// <summary>Trims only.</summary>
    public const string Trim = "trim";

    /// <summary>The file that, made in the session the script runs in, asks it to stop at the next safe place.</summary>
    public const string StopFlag = "/tmp/wslc-agent-stop";

    /// <summary>What starts every line the script means to be read.</summary>
    public const string Mark = "::";

    private const int Base64LineLength = 76;

    public const string Text = """
        mode=$1
        root=/var/lib/docker
        db=$root/image/overlay2
        layers=$db/layerdb/sha256
        say() { echo "::$*"; }
        # Asked to stop from outside (a file made in this session): it leaves
        # between two pieces of work, never in the middle of one, so what it
        # leaves behind is a store with fewer layers and nothing half removed.
        stop=/tmp/wslc-agent-stop
        rm -f $stop
        halt() {
          [ -e $stop ] || return 1
          rm -rf $work
          say "STOPPED"
          say "END"
          exit 0
        }
        trim() {
          say "STEP Trimming (fstrim -av)"
          said=$(mktemp)
          fstrim -av > $said 2>&1 &
          trimming=$!
          seconds=0
          while kill -0 $trimming 2>/dev/null; do
            sleep 1
            seconds=$((seconds + 1))
            say "TICK Trimming, $seconds s"
            if [ -e $stop ]; then kill $trimming 2>/dev/null; rm -f $said; halt; fi
          done
          while read -r line; do say "TRIM $line"; done < $said
          rm -f $said
        }
        if [ "$mode" = trim ]; then trim; say "END"; exit 0; fi
        if [ ! -d $layers ] || [ ! -d $db/imagedb/content/sha256 ] || [ ! -d $root/overlay2/l ]; then
          say "LAYOUT unknown"
          [ "$mode" = report ] || trim
          say "END"
          exit 0
        fi
        work=$(mktemp -d)
        hex() { case $1 in *[!a-f0-9]*|'') return 1;; esac; [ ${#1} -eq 64 ]; }
        plain() { case $1 in *[!a-z0-9-]*|''|l) return 1;; esac; }

        # Every layer an image is made of: the chain of each one's diff ids.
        say "STEP Reading the layers of the images"
        for config in $db/imagedb/content/sha256/*; do
          [ -f "$config" ] || continue
          tr -d '\n' < "$config" | grep -o '"diff_ids":\[[^]]*\]' | head -n 1 | grep -o 'sha256:[a-f0-9]\{64\}' | {
            chain=
            while read -r diff; do
              if [ -z "$chain" ]; then chain=${diff#sha256:}
              else chain=$(printf 'sha256:%s %s' "$chain" "$diff" | sha256sum | cut -c1-64); fi
              echo "$chain"
            done
          }
        done > $work/used
        images=$(ls $db/imagedb/content/sha256 | wc -l)

        # Every layer a container that is still there stands on, with the ones
        # under it. A writable layer whose container is gone is left out: it is
        # what keeps the layers of images long removed.
        say "STEP Reading the layers of the containers"
        : > $work/keepdirs
        : > $work/stale
        containers=0
        for mount in $db/layerdb/mounts/*; do
          [ -d "$mount" ] || continue
          name=$(basename "$mount")
          if hex "$name" && [ ! -d "$root/containers/$name" ]; then echo "$name" >> $work/stale; continue; fi
          containers=$((containers + 1))
          for id in mount-id init-id; do
            [ -f "$mount/$id" ] && { cat "$mount/$id"; echo; } >> $work/keepdirs
          done
          [ -f "$mount/parent" ] || continue
          chain=$(cat "$mount/parent"); chain=${chain#sha256:}
          while hex "$chain"; do
            echo "$chain"
            if [ -f $layers/$chain/parent ]; then chain=$(cat $layers/$chain/parent); chain=${chain#sha256:}; else chain=; fi
          done
        done >> $work/used
        grep -o 'overlay2/[a-z0-9-]*' /proc/mounts | sed 's#overlay2/##' >> $work/keepdirs
        sort -u $work/used > $work/used.sorted
        ls $layers | sort > $work/all
        comm -23 $work/all $work/used.sorted > $work/unused
        comm -12 $work/all $work/used.sorted > $work/kept

        say "STEP Adding up $(wc -l < $work/all) layers"
        keptBytes=0
        while read -r chain; do
          { cat $layers/$chain/cache-id; echo; } >> $work/keepdirs
          size=$(cat $layers/$chain/size 2>/dev/null); keptBytes=$((keptBytes + ${size:-0}))
        done < $work/kept
        unusedBytes=0
        unusedCount=0
        : > $work/unuseddirs
        while read -r chain; do
          { cat $layers/$chain/cache-id; echo; } >> $work/unuseddirs
          size=$(cat $layers/$chain/size 2>/dev/null); unusedBytes=$((unusedBytes + ${size:-0}))
          unusedCount=$((unusedCount + 1))
        done < $work/unused

        # What is left in the layer folder is no layer's and no container's:
        # the build cache's own folders and the writable layers left behind.
        sort -u $work/keepdirs > $work/keepdirs.sorted
        sort -u $work/unuseddirs > $work/unuseddirs.sorted
        ls $root/overlay2 | grep -v '^l$' | sort > $work/dirs
        comm -23 $work/dirs $work/keepdirs.sorted | comm -23 - $work/unuseddirs.sorted > $work/strays
        strayCount=$(wc -l < $work/strays)
        say "STEP Measuring the build cache ($strayCount folders)"
        strayKb=0
        measured=0
        while read -r dir; do
          plain "$dir" || continue
          kb=$(du -sxk "$root/overlay2/$dir" 2>/dev/null | cut -f1); strayKb=$((strayKb + ${kb:-0}))
          measured=$((measured + 1))
          say "TICK $measured of $strayCount folders measured"
          halt
        done < $work/strays
        cacheKb=0
        [ -d $root/buildkit ] && cacheKb=$(du -sxk $root/buildkit | cut -f1)
        usedKb=$(df -k $root | awk 'NR==2 {print $3}')

        say "USED $((usedKb * 1024))"
        say "IMAGES $images $(wc -l < $work/kept) $keptBytes"
        say "CONTAINERS $containers $(wc -l < $work/stale)"
        say "UNUSED $unusedCount $unusedBytes"
        say "BUILD $strayCount $(((strayKb + cacheKb) * 1024))"

        drop() {
          plain "$1" || return 0
          [ -f "$root/overlay2/$1/link" ] && rm -f "$root/overlay2/l/$(cat "$root/overlay2/$1/link")"
          rm -rf "$root/overlay2/$1"
        }
        halt
        if [ "$mode" = clean ]; then
          removed=0
          while read -r chain; do
            hex "$chain" || continue
            drop "$(cat $layers/$chain/cache-id)"
            rm -rf "$layers/$chain"
            removed=$((removed + 1))
            say "TICK $removed of $unusedCount layers removed"
            halt
          done < $work/unused
          say "STEP Removing the build cache"
          dropped=0
          while read -r dir; do
            drop "$dir"
            dropped=$((dropped + 1))
            say "TICK $dropped of $strayCount folders removed"
            halt
          done < $work/strays
          while read -r name; do
            hex "$name" && rm -rf "$db/layerdb/mounts/$name"
          done < $work/stale
          rm -rf $root/buildkit
          afterKb=$(df -k $root | awk 'NR==2 {print $3}')
          say "FREED $(((usedKb - afterKb) * 1024))"
          trim
        fi
        rm -rf $work
        say "END"
        """;

    /// <summary>
    /// The one shell line that runs the script in <paramref name="mode"/> in a
    /// session that is up (<c>wslc system session run sh -c</c>): there is no
    /// terminal there to cut a long line, and base64 keeps the script's quotes
    /// away from the command line.
    /// </summary>
    public static string Piped(string mode) => GuestScript.Piped(Text, mode);

    /// <summary>
    /// What is typed into the temporary session's shell to run the script in
    /// <paramref name="mode"/> and leave: the script goes in as base64 through a
    /// here-document, in short lines, since a terminal cuts a typed line at
    /// 4096 characters and the shell would read the script's own quotes.
    /// </summary>
    public static string Typed(string mode)
    {
        var encoded = Encoded();
        var typed = new StringBuilder($"base64 -d > {GuestPath} <<'EOF'\n");
        for (var at = 0; at < encoded.Length; at += Base64LineLength)
        {
            typed.Append(encoded, at, Math.Min(Base64LineLength, encoded.Length - at)).Append('\n');
        }

        return typed.Append("EOF\n").Append($"sh {GuestPath} {mode}; exit\n").ToString();
    }

    private static string Encoded() => Convert.ToBase64String(Encoding.UTF8.GetBytes(Text.ReplaceLineEndings("\n")));
}
