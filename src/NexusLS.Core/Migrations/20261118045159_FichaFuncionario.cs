using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class FichaFuncionario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Complemento",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContatoEmergenciaNome",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContatoEmergenciaTelefone",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataAdmissao",
                table: "Funcionarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataNascimento",
                table: "Funcionarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Endereco",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Foto",
                table: "Funcionarios",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Numero",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rg",
                table: "Funcionarios",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SalarioCentavos",
                table: "Funcionarios",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Complemento",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "ContatoEmergenciaNome",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "ContatoEmergenciaTelefone",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "DataAdmissao",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "DataNascimento",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Endereco",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Foto",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Rg",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "SalarioCentavos",
                table: "Funcionarios");
        }
    }
}
