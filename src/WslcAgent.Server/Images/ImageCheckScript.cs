using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The shell script that checks images inside the running session, where the
/// session's store is. <c>wslc</c> has no command that verifies an image, and
/// one whose pull was cut short, or whose disk filled up, is listed whole and
/// fails only when a container made from it runs. The session records, for
/// each layer, the size of what the layer holds; the script adds up what is
/// there and compares.
/// <para>
/// A layer is whole when its files, each counted once however many names it
/// has, add up to the recorded size — alone, or with the length of what its
/// links point at, since the two ways of making a layer count links
/// differently. Measured on a store of 197 layers, every one matched one of
/// the two; a layer that matches neither has lost something.
/// </para>
/// Every line it means to be read starts with <see cref="Mark"/>: a
/// <c>LAYER</c> as each one is measured, so a check under way says how far it
/// has got; a <c>BAD</c> for one that lost something; an <c>IMAGE</c> when
/// its layers are done. An image is named by the first twelve characters of
/// its id, as the image list names it.
/// </summary>
public static class ImageCheckScript
{
    /// <summary>What starts every line the script means to be read.</summary>
    public const string Mark = "::";

    public const string Text = """
        export LC_ALL=C
        root=/var/lib/docker
        db=$root/image/overlay2
        layers=$db/layerdb/sha256
        say() { echo "::$*"; }
        if [ ! -d $layers ] || [ ! -d $db/imagedb/content/sha256 ] || [ ! -d $root/overlay2 ]; then
          say "LAYOUT unknown"
          say "END"
          exit 0
        fi
        work=$(mktemp -d)

        # A layer is measured once, however many images stand on it.
        measure() {
          [ -f $work/$1 ] && return
          id=$(cat $layers/$1/cache-id 2>/dev/null)
          want=$(cat $layers/$1/size 2>/dev/null)
          want=${want:-0}
          if [ -z "$id" ] || [ ! -d "$root/overlay2/$id/diff" ]; then
            echo "bad ${id:-none} $want 0" > $work/$1
            return
          fi
          files=$(find "$root/overlay2/$id/diff" -xdev -type f -printf '%i %s\n' | sort -u -k1,1 | awk '{s+=$2} END {print s+0}')
          links=$(find "$root/overlay2/$id/diff" -xdev -type l -printf '%l\n' | awk '{s+=length($0)} END {print s+0}')
          if [ "$files" -eq "$want" ] || [ $((files + links)) -eq "$want" ]; then
            echo "ok $id $want $files" > $work/$1
          else
            echo "bad $id $want $files" > $work/$1
          fi
        }

        # What was asked for, as image ids: an id as it is, a reference through
        # the store's own list of names.
        wanted=
        for asked in "$@"; do
          case $asked in
            *[!a-f0-9]*) found=$(awk -v key="\"$asked\":\"sha256:" '{at = index($0, key); if (at) print substr($0, at + length(key), 64)}' $db/repositories.json 2>/dev/null | head -n 1) ;;
            *) found=$asked ;;
          esac
          if [ -n "$found" ]; then wanted="$wanted $found"; else say "MISSING $asked"; fi
        done

        for config in $db/imagedb/content/sha256/*; do
          [ -f "$config" ] || continue
          image=$(basename "$config")
          if [ $# -gt 0 ]; then
            asked=
            for id in $wanted; do case $image in "$id"*) asked=yes;; esac; done
            [ -n "$asked" ] || continue
          fi
          short=$(echo $image | cut -c1-12)
          diffs=$(tr -d '\n' < "$config" | grep -o '"diff_ids":\[[^]]*\]' | head -n 1 | grep -o 'sha256:[a-f0-9]\{64\}')
          total=$(echo $diffs | wc -w)
          count=0
          damaged=0
          chain=
          for diff in $diffs; do
            if [ -z "$chain" ]; then chain=${diff#sha256:}
            else chain=$(printf 'sha256:%s %s' "$chain" "$diff" | sha256sum | cut -c1-64); fi
            count=$((count + 1))
            measure $chain
            read -r state id want have < $work/$chain
            say "LAYER $short $count $total $state"
            if [ "$state" != ok ]; then
              damaged=$((damaged + 1))
              say "BAD $short $id $want $have"
            fi
          done
          say "IMAGE $short $count $damaged"
        done
        rm -rf $work
        say "END"
        """;

    /// <summary>The line that checks the images whose ids start with <paramref name="ids"/>, or every image when there are none.</summary>
    public static string Piped(IReadOnlyList<string> ids) => GuestScript.Piped(Text, [.. ids]);

    /// <summary>
    /// The line that checks the one image given after it on the command line
    /// (<c>sh -c LINE IMAGE</c>), by its reference or its id: the image reaches
    /// the script as an argument of its own, never as text inside the line, so
    /// nothing in it is run. It is left unquoted there: a reference has no
    /// spaces, and a quote would have to survive the command line of Windows.
    /// </summary>
    public static string ForArgument { get; } = GuestScript.Piped(Text, "$0");
}