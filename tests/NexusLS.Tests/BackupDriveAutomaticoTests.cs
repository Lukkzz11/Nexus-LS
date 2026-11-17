using NexusLS.Core.Backup;
using Xunit;

namespace NexusLS.Tests;

public class BackupDriveAutomaticoTests
{
    [Fact]
    public void EnviarAoDrive_vem_ligado_e_persiste_desligado()
    {
        var arq = Path.Combine(Path.GetTempPath(), "nexus-bkp-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoBackupStore(arq);
            Assert.True(store.Carregar().EnviarAoDrive);
            var c = store.Carregar();
            c.EnviarAoDrive = false;
            store.Salvar(c);
            Assert.False(new ConfiguracaoBackupStore(arq).Carregar().EnviarAoDrive);
        }
        finally { File.Delete(arq); }
    }
}
