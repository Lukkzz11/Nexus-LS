using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class DesignerViewModel
{
    // =========================================================================
    // SISTEMA DE HISTÓRICO UNDO / REDO (CTRL+Z E CTRL+SHIFT+Z / CTRL+Y)
    // =========================================================================
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _isRestoringState = false;

    public void RegistrarSnapshot()
    {
        if (_isRestoringState || Modelo == null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _undoStack.Push(json);
            if (_undoStack.Count > 60)
            {
                var list = _undoStack.ToList();
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (_undoStack.Count < 50) _undoStack.Push(list[i]);
                }
            }
            _redoStack.Clear();
        }
        catch (Exception exLog) { NexusLS.BarTender.App.Log($"DesignerViewModel.UndoRedo #1: {exLog.Message}"); }
    }

    [RelayCommand]
    public void Desfazer()
    {
        if (_undoStack.Count == 0 || Modelo == null) return;
        _isRestoringState = true;
        try
        {
            var estadoAtual = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _redoStack.Push(estadoAtual);

            var estadoAnterior = _undoStack.Pop();
            RestaurarModelo(estadoAnterior);
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    [RelayCommand]
    public void Refazer()
    {
        if (_redoStack.Count == 0 || Modelo == null) return;
        _isRestoringState = true;
        try
        {
            var estadoAtual = System.Text.Json.JsonSerializer.Serialize(Modelo, ModeloEtiquetaDesign.JsonOpts);
            _undoStack.Push(estadoAtual);

            var proximoEstado = _redoStack.Pop();
            RestaurarModelo(proximoEstado);
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private void RestaurarModelo(string json)
    {
        try
        {
            var mod = System.Text.Json.JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts);
            if (mod != null)
            {
                Modelo = mod;
                InscreverEventosModelo();
                OnPropertyChanged(nameof(FolhaLarguraMm));
                OnPropertyChanged(nameof(FolhaAlturaMm));
                ElementoSelecionado = null;
                ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
                AtualizarPreview();
            }
        }
        catch (Exception exLog) { NexusLS.BarTender.App.Log($"DesignerViewModel.UndoRedo #2: {exLog.Message}"); }
    }
}
