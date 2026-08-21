namespace SiYuanSync.Core.Siyuan;

internal static class SiyuanEndpoints
{
    public const string ListNotebooks = "/api/notebook/lsNotebooks";
    public const string GetIdsByHPath = "/api/filetree/getIDsByHPath";
    public const string CreateDocWithMd = "/api/filetree/createDocWithMd";
    public const string RenameDocById = "/api/filetree/renameDocByID";
    public const string RemoveDocById = "/api/filetree/removeDocByID";
    public const string GetChildBlocks = "/api/block/getChildBlocks";
    public const string DeleteBlock = "/api/block/deleteBlock";
    public const string PrependBlock = "/api/block/prependBlock";
    public const string SetDocSortMode = "/api/filetree/setDocSortMode";
}
