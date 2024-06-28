using Cw6.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Cw6.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly string _connectionString;

        public WarehouseController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost]
        public async Task<IActionResult> AddProductToWarehouse(ProductWarehouseRequest request)
        {
            if (request == null ||
                request.IdProduct <= 0 ||
                request.IdWarehouse <= 0 ||
                request.Amount <= 0 ||
                request.CreatedAt == default)
            {
                return BadRequest("Nieprawidłowy format żądania lub brak wymaganych pól.");
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // 1. Sprawdzenie czy produkt istnieje
                    bool productExist = await CheckIfProductExist(connection, request.IdProduct);
                    if (!productExist)
                    {
                        return NotFound("Produkt o podanym IdProduct nie istnieje.");
                    }

                    // 2. Sprawdzenie czy magazyn istnieje
                    bool wareHouseExist = await CheckIfWarehouseExist(connection, request.IdWarehouse);
                    if (!wareHouseExist)
                    {
                        return NotFound("Magazyn o podanym IdWarehouse nie istnieje.");
                    }

                    // 3. Sprawdzenie czy istnieje odpowiednie zamówienie
                    Order order = await GetMatchingOrder(connection, request);
                    if (order == null)
                    {
                        return BadRequest("Nie znaleziono odpowiedniego zamówienia dla podanego produktu i ilości.");
                    }

                    // 4. Sprawdzenie czy zamówienie nie zostało już zrealizowane
                    bool isOrderFulfilled = await CheckIfOrderFulfilled(connection, order.IdOrder);
                    if (isOrderFulfilled)
                    {
                        return BadRequest("Zamówienie zostało już zrealizowane.");
                    }

                    // 5. Aktualizacja zamówienia na zrealizowane
                    bool orderUpdateSuccess = await UpdateOrderFulfilledAt(connection, order.IdOrder);
                    if (!orderUpdateSuccess)
                    {
                        return StatusCode(500, "Nie udało się zaktualizować statusu realizacji zamówienia.");
                    }

                    // 6. Wstawienie rekordu do tabeli Product_Warehouse
                    int productWarehouseId = await InsertProductWarehouseRecord(connection, request, order);
                    if (productWarehouseId <= 0)
                    {
                        return StatusCode(500, "Nie udało się wstawić rekordu do tabeli Product_Warehouse.");
                    }

                    // 7. Zwrócenie wartości klucza głównego wygenerowanego dla rekordu w Product_Warehouse
                    return Ok(new { IdProductWarehouse = productWarehouseId });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Wystąpił błąd podczas przetwarzania żądania: {ex.Message}");
            }
        }

        [HttpPost("add-via-proc")]
        public async Task<IActionResult> AddProductToWarehouseViaProc(ProductWarehouseRequest request)
        {
            if (request == null ||
                request.IdProduct <= 0 ||
                request.IdWarehouse <= 0 ||
                request.Amount <= 0 ||
                request.CreatedAt == default)
            {
                return BadRequest("Nieprawidłowy format żądania lub brak wymaganych pól.");
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand("AddProductToWarehouse", connection))
                    {
                        command.CommandType = System.Data.CommandType.StoredProcedure;

                        command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                        command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                        command.Parameters.AddWithValue("@Amount", request.Amount);
                        command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

                        SqlParameter newIdParam = new SqlParameter("@NewId", System.Data.SqlDbType.Int)
                        {
                            Direction = System.Data.ParameterDirection.Output
                        };
                        command.Parameters.Add(newIdParam);

                        await command.ExecuteNonQueryAsync();

                        if (newIdParam.Value == DBNull.Value)
                        {
                            return StatusCode(500, "Nie udało się uzyskać wartości klucza głównego nowego rekordu.");
                        }

                        return Ok(new { IdProductWarehouse = Convert.ToInt32(newIdParam.Value) });
                    }
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 50000)  // Sprawdzenie błędu RAISERROR
                {
                    return BadRequest(ex.Message);
                }
                return StatusCode(500, $"Błąd SQL: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Wystąpił błąd podczas przetwarzania żądania: {ex.Message}");
            }
        }


        private async Task<bool> CheckIfProductExist(SqlConnection connection, int idProduct)
        {
            string query = "SELECT 1 FROM Product WHERE IdProduct = @IdProduct";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdProduct", idProduct);
                return await command.ExecuteScalarAsync() != null;
            }
        }

        private async Task<bool> CheckIfWarehouseExist(SqlConnection connection, int idWarehouse)
        {
            string query = "SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdWarehouse", idWarehouse);
                return await command.ExecuteScalarAsync() != null;
            }
        }

        private async Task<Order> GetMatchingOrder(SqlConnection connection, ProductWarehouseRequest request)
        {
            string query = @"
                SELECT IdOrder, IdProduct, Amount, CreatedAt
                FROM [Order]
                WHERE IdProduct = @IdProduct 
                AND Amount = @Amount 
                AND CreatedAt < @CreatedAt 
                AND FulfilledAt IS NULL 
                AND NOT EXISTS (
                    SELECT 1 FROM Product_Warehouse WHERE IdOrder = [Order].IdOrder
                )";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new Order
                        {
                            IdOrder = reader.GetInt32(0),
                            IdProduct = reader.GetInt32(1),
                            Amount = reader.GetInt32(2),
                            CreatedAt = reader.GetDateTime(3)
                        };
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        private async Task<bool> CheckIfOrderFulfilled(SqlConnection connection, int orderId)
        {
            string query = "SELECT 1 FROM Product_Warehouse WHERE IdOrder = @IdOrder";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdOrder", orderId);
                return await command.ExecuteScalarAsync() != null;
            }
        }

        private async Task<bool> UpdateOrderFulfilledAt(SqlConnection connection, int orderId)
        {
            string query = "UPDATE [Order] SET FulfilledAt = GETDATE() WHERE IdOrder = @IdOrder";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdOrder", orderId);
                return await command.ExecuteNonQueryAsync() > 0;
            }
        }

        private async Task<int> InsertProductWarehouseRecord(SqlConnection connection, ProductWarehouseRequest request, Order order)
        {
            string query = @"
                INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
                VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Price, @CreatedAt);
                SELECT SCOPE_IDENTITY();";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                command.Parameters.AddWithValue("@IdOrder", order.IdOrder);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@Price", request.Amount * order.Amount);  // Assuming price is stored in Order table
                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);  // Use current UTC time

                object result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }
    }
}
