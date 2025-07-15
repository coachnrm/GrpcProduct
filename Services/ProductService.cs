using System;
using Grpc.Core;
using GrpcProduct.Data;
using GrpcProduct.Models;
using GrpcProductService;
using Microsoft.EntityFrameworkCore;

namespace GrpcProduct.Services;

public class ProductService : Product.ProductBase
{
    private readonly AppDbContext _context;

    public ProductService(AppDbContext context)
    {
        _context = context;
    }


    public override async Task<CreateProductResponse> CreateProduct(CreateProductRequest request,
        ServerCallContext context)
    {
        if (request == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Request is null"));
        }

        var productItem = new Products
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Price = Convert.ToDecimal(request.Price),
            Created = DateTime.Now,
            Updated = DateTime.Now,
            Tags = request.Tag
        };
        _context.Products.Add(productItem);
        await _context.SaveChangesAsync();

        return new CreateProductResponse
        {
            Success = true,
            Message = "Product successfully created",
            Product = new ProductModel
            {
                Id = productItem.Id.ToString(),
                Name = productItem.Name,
                Description = productItem.Description,
                Price = Convert.ToDouble(request.Price),
                CreatedAt = productItem.Created.ToString("o"),
                UpdatedAt = productItem.Updated.ToString("o"),
                Tag = productItem.Tags

            }

        };
    }

    public override async Task<GetProductResponse> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (request == null)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, " Request is null"));
        }

        if (!Guid.TryParse(request.Id, out var productId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Id is invalid"));
        }

        var productItem = await _context.Products.FindAsync(productId);
        if (productItem is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Product not found"));
        }

        return new GetProductResponse()
        {
            Success = true,
            Message = "Product retrieved successfully",
            Product = new ProductModel
            {
                Id = productItem.Id.ToString(),
                Name = productItem.Name,
                Description = productItem.Description,
                Price = Convert.ToDouble(productItem.Price),
                CreatedAt = productItem.Created.ToString("o"),
                UpdatedAt = productItem.Updated.ToString("o"),
                Tag = productItem.Tags
            }

        };
    }

    public override async Task<ListProductsResponse> ListProduct(ListProductsRequest request, ServerCallContext context)
    {
        var pageSize = request.PageSize <= 0 ? 10 : request.PageSize;
        var page = request.Page <= 0 ? 1 : request.Page;

        // Get total Count for pagination 
        var totalCount = await _context.Products.CountAsync();
        var productItemList = await _context.Products
            .Skip((page - 1) * pageSize).
            Take(pageSize).
            ToListAsync();
        var response = new ListProductsResponse
        {
            Success = true,
            Message = productItemList.Any() ? "Products retrieved successfully" : "No products found",
            TotalCount = totalCount

        };

        // Convert product to ProductModel and add to response 

        foreach (var item in productItemList)
        {
            response.Products.Add(new ProductModel
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Description = item.Description,
                Price = Convert.ToDouble(item.Price),
                CreatedAt = item.Created.ToString("o"),
                UpdatedAt = item.Updated.ToString("o"),
                Tag = item.Tags
            });
        }
        return response;

    }

    public override async Task<UpdateProductResponse> UpdateProduct(UpdateProductRequest request,
        ServerCallContext context)
    {
        if (request == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Request is null"));

        }


        if (!Guid.TryParse(request.Id, out Guid productId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid product ID format"));
        }

        var productItem = await _context.Products.FindAsync(productId);
        if (productItem == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Product not found"));

        }
        // Update  the properties 
        productItem.Name = request.Name;
        productItem.Description = request.Description;
        productItem.Price = Convert.ToDecimal(request.Price);
        productItem.Tags = request.Tag;
        productItem.Updated = DateTime.Now;
        await _context.SaveChangesAsync();

        try
        {
            await _context.SaveChangesAsync();
            // Return the new response 

            return new UpdateProductResponse
            {
                Success = true,
                Message = "Product updated successfully",
                Product = new ProductModel
                {
                    Id = productItem.Id.ToString(),
                    Name = productItem.Name,
                    Description = productItem.Description,
                    Price = Convert.ToDouble(productItem.Price),
                    CreatedAt = productItem.Created.ToString("o"),
                    UpdatedAt = productItem.Updated.ToString("o"),
                    Tag = productItem.Tags
                }
            };

        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to update product: {ex.Message}"));

        }



    }


    public override async Task<DeleteProductResponse> DeleteProduct(DeleteProductRequest request, ServerCallContext context)
    {
        if (request is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Request is null"));

        }
        if (!Guid.TryParse(request.Id, out Guid productId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid product ID format"));
        }

        var productItem = await _context.Products.FindAsync(productId);
        if (productItem == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Product not found"));
        }


        try
        {
            _context.Products.Remove(productItem);
            await _context.SaveChangesAsync();

            return new DeleteProductResponse
            {
                Success = true,
                Message = "Product deleted successfully",
            };
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to delete product: {ex.Message}"));
        }
    }
    
    public override async Task ChatProduct(IAsyncStreamReader<ProductModel> requestStream, 
    IServerStreamWriter<ProductModel> responseStream, 
    ServerCallContext context)
    {
        // Read incoming messages from client
        var readTask = Task.Run(async () =>
        {
            await foreach (var product in requestStream.ReadAllAsync())
            {
                Console.WriteLine($"Received from client: {product.Name}");
                // Process the incoming message
                // You can send a response immediately if needed:
                // await responseStream.WriteAsync(new ProductModel { ... });
            }
        });

        // Write outgoing messages to client
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var productUpdate = new ProductModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = "AutoUpdate",
                Description = "Update from server",
                Price = 99.99,
                CreatedAt = DateTime.Now.ToString("o"),
                UpdatedAt = DateTime.Now.ToString("o"),
                Tag = "streamed"
            };

            await responseStream.WriteAsync(productUpdate);
            await Task.Delay(5000);
        }

        await readTask;
    }

}
