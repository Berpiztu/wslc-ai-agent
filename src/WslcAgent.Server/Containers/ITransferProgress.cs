namespace WslcAgent.Server.Containers;

/// <summary>
/// What a file on its way in or out tells while it happens, so
/// <see cref="ContainerFiles"/> stays about files and knows nothing about the
/// jobs the agent keeps: how big the file is once that is known, how much of
/// the stage under way is done, and each step it moves on to.
/// <see cref="ContainerTransfers"/> is what the endpoint hands it.
/// </summary>
public interface ITransferProgress
{
    /// <summary>The file's size, learned from the container: a download knows it only after it has asked.</summary>
    void Total(long bytes);

    /// <summary>How much of the stage under way is done.</summary>
    void Progress(long bytes);

    /// <summary>An upload: the bytes are all in, and <c>wslc container cp</c> is putting the file into the container.</summary>
    void Copying();

    /// <summary>A download: the file is out of the container and its bytes are going to the client.</summary>
    void Sending();
}
